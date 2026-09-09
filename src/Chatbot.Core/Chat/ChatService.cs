using Chatbot.Core.Rag;
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
    private readonly RetrievalContext _retrieved;
    private readonly ChatbotOptions _options;
    private readonly ILogger<ChatService> _logger;

    public ChatService(
        IChatClient chatClient,
        IConversationStore conversations,
        IEnumerable<IChatToolProvider> toolProviders,
        RetrievalContext retrieved,
        IOptions<ChatbotOptions> options,
        ILogger<ChatService> logger)
    {
        _chatClient = chatClient;
        _conversations = conversations;
        _toolProviders = toolProviders;
        _retrieved = retrieved;
        _options = options.Value;
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

        var history = await _conversations.GetAsync(conversationId, cancellationToken);
        var userMessage = new ChatMessage(ChatRole.User, request.Message);

        // Systemprompten gemmes ikke i historikken — så slår en rettelse i konfigurationen
        // igennem med det samme, også i igangværende samtaler.
        List<ChatMessage> prompt =
        [
            new ChatMessage(ChatRole.System, _options.SystemPrompt),
            .. Trim(history),
            userMessage,
        ];

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
        var reply = response.Text;

        // Kun brugerens spørgsmål og det endelige svar gemmes — ikke tool-kald og tool-resultater.
        // De kan være store (hele chunks), og modellen henter dem igen, hvis den får brug for dem.
        await _conversations.AppendAsync(
            conversationId,
            [userMessage, new ChatMessage(ChatRole.Assistant, reply)],
            cancellationToken);

        var sources = _retrieved.Hits
            .GroupBy(h => (h.Chunk.Source, h.Chunk.Heading))
            .Select(g => new SourceReference(g.Key.Source, g.Key.Heading, g.Max(h => h.Score)))
            .OrderByDescending(s => s.Score)
            .ToList();

        return new ChatTurnResult(reply, conversationId, sources);
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
