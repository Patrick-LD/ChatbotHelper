using Chatbot.Core.Actions;
using Chatbot.Core.Rag;
using Chatbot.Core.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chatbot.Core.Chat;

/// <inheritdoc />
public sealed class ChatService : IChatService
{
    private readonly IChatClient _chatClient;
    private readonly IConversationStore _conversations;
    private readonly IEnumerable<IChatToolProvider> _toolProviders;
    private readonly IEnumerable<IActionExecutor> _executors;
    private readonly IPendingActionStore _pending;
    private readonly RetrievalContext _retrieved;
    private readonly TurnContext _turn;
    private readonly ChatbotOptions _options;
    private readonly TimeSpan _pendingTimeout;
    private readonly ILogger<ChatService> _logger;

    public ChatService(
        IChatClient chatClient,
        IConversationStore conversations,
        IEnumerable<IChatToolProvider> toolProviders,
        IEnumerable<IActionExecutor> executors,
        IPendingActionStore pending,
        RetrievalContext retrieved,
        TurnContext turn,
        IOptions<ChatbotOptions> options,
        IOptions<ToolsOptions> toolsOptions,
        ILogger<ChatService> logger)
    {
        _chatClient = chatClient;
        _conversations = conversations;
        _toolProviders = toolProviders;
        _executors = executors;
        _pending = pending;
        _retrieved = retrieved;
        _turn = turn;
        _options = options.Value;
        _pendingTimeout = TimeSpan.FromMinutes(toolsOptions.Value.PendingActionTimeoutMinutes);
        _logger = logger;
    }

    public async Task<ChatTurnResult> SendAsync(ChatTurnRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            throw new ArgumentException("Beskeden må ikke være tom.", nameof(request));
        }

        var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
            ? Guid.NewGuid().ToString("n")
            : request.ConversationId;

        _turn.ConversationId = conversationId;
        var userMessage = new ChatMessage(ChatRole.User, request.Message);

        // Bekræftelses-flowet (fase 3.3) afgøres HER, før modellen ser beskeden. Et klart "ja" udfører
        // den ventende handling uden modelkald; et klart "nej" annullerer. Alt andet går til modellen
        // sammen med en note om, hvad der venter, så brugeren kan rette oplysningerne.
        var pending = await GetValidPendingAsync(conversationId, cancellationToken);
        if (pending is not null)
        {
            switch (ConfirmationParser.Parse(request.Message))
            {
                case ConfirmationIntent.Confirm:
                {
                    var reply = await ExecuteAsync(conversationId, pending, cancellationToken);
                    await _conversations.AppendAsync(conversationId, [userMessage, new ChatMessage(ChatRole.Assistant, reply)], cancellationToken);
                    return new ChatTurnResult(reply, conversationId, []);
                }

                case ConfirmationIntent.Reject:
                {
                    await _pending.ClearAsync(conversationId, cancellationToken);
                    _logger.LogInformation("Handling annulleret af brugeren i samtale {ConversationId}: {Summary}", conversationId, pending.Summary);
                    const string reply = "Okay, jeg har annulleret handlingen. Intet er oprettet. Sig til, hvis du vil have mig til at gøre noget andet.";
                    await _conversations.AppendAsync(conversationId, [userMessage, new ChatMessage(ChatRole.Assistant, reply)], cancellationToken);
                    return new ChatTurnResult(reply, conversationId, []);
                }
            }
        }

        var history = await _conversations.GetAsync(conversationId, cancellationToken);

        // Systemprompten gemmes ikke i historikken — så slår en rettelse i konfigurationen
        // igennem med det samme, også i igangværende samtaler.
        List<ChatMessage> prompt =
        [
            new ChatMessage(ChatRole.System, _options.SystemPrompt),
            .. Trim(history),
        ];

        if (pending is not null)
        {
            prompt.Add(new ChatMessage(
                ChatRole.System,
                $"Der venter en handling på brugerens bekræftelse: \"{pending.Summary}\". Brugeren har hverken " +
                "bekræftet eller afvist entydigt. Retter brugeren oplysninger, så kald toolet igen med de rettede " +
                "oplysninger (det erstatter det gamle forslag). Ellers svar på beskeden og mind om, at handlingen venter."));
        }

        prompt.Add(userMessage);

        // Tools gives med i hvert kald. Modellen vælger selv, om den kalder et — og
        // IChatClient-pipelinen (UseFunctionInvocation) udfører kaldet og sender resultatet
        // tilbage til modellen, før det endelige svar kommer hertil.
        var tools = _toolProviders.SelectMany(p => p.GetTools()).ToList();
        var chatOptions = tools.Count > 0 ? new ChatOptions { Tools = tools } : null;

        _logger.LogInformation(
            "Kalder model for samtale {ConversationId} med {MessageCount} beskeder og {ToolCount} tools.",
            conversationId,
            prompt.Count,
            tools.Count);

        var response = await _chatClient.GetResponseAsync(prompt, chatOptions, cancellationToken);

        // llama3.1 svarer af og til med {"type":"message","text":"…"} i stedet for tekst, når den har
        // flere tools. Brugeren skal ikke se rå JSON (fund i fase 3-evalueringen, Q12/Q20).
        var replyText = ReplySanitizer.Unwrap(response.Text);

        // Kun brugerens spørgsmål og det endelige svar gemmes — ikke tool-kald og tool-resultater.
        await _conversations.AppendAsync(
            conversationId,
            [userMessage, new ChatMessage(ChatRole.Assistant, replyText)],
            cancellationToken);

        var sources = _retrieved.Hits
            .GroupBy(h => (h.Chunk.Source, h.Chunk.Heading))
            .Select(g => new SourceReference(g.Key.Source, g.Key.Heading, g.Max(h => h.Score)))
            .OrderByDescending(s => s.Score)
            .ToList();

        // Et tool kan have forberedt (eller erstattet) en handling under modelkaldet.
        var nowPending = await _pending.GetAsync(conversationId, cancellationToken);

        return new ChatTurnResult(replyText, conversationId, sources, nowPending?.Summary);
    }

    private async Task<PendingAction?> GetValidPendingAsync(string conversationId, CancellationToken cancellationToken)
    {
        var pending = await _pending.GetAsync(conversationId, cancellationToken);
        if (pending is null)
        {
            return null;
        }

        if (DateTimeOffset.UtcNow - pending.CreatedAt > _pendingTimeout)
        {
            _logger.LogInformation("Ventende handling udløbet i samtale {ConversationId}: {Summary}", conversationId, pending.Summary);
            await _pending.ClearAsync(conversationId, cancellationToken);
            return null;
        }

        return pending;
    }

    private async Task<string> ExecuteAsync(string conversationId, PendingAction pending, CancellationToken cancellationToken)
    {
        var executor = _executors.FirstOrDefault(e => e.ToolName == pending.ToolName)
            ?? throw new InvalidOperationException($"Ingen executor registreret for toolet '{pending.ToolName}'.");

        // Ryd FØR udførelsen, så et gentaget "ja" ikke kan udføre handlingen to gange.
        await _pending.ClearAsync(conversationId, cancellationToken);

        _logger.LogInformation("Udfører bekræftet handling i samtale {ConversationId}: {Summary}", conversationId, pending.Summary);

        return await executor.ExecuteAsync(pending.ParametersJson, cancellationToken);
    }

    /// <summary>Beholder kun de seneste beskeder, så prompten ikke vokser i det uendelige.</summary>
    private IEnumerable<ChatMessage> Trim(IReadOnlyList<ChatMessage> history)
    {
        var max = _options.MaxHistoryMessages;
        if (max <= 0 || history.Count <= max)
        {
            return history;
        }

        return history.Skip(history.Count - max);
    }
}
