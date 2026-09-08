using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chatbot.Core.Chat;

/// <inheritdoc />
public sealed class ChatService : IChatService
{
    private readonly IChatClient _chatClient;
    private readonly IConversationStore _conversations;
    private readonly ChatbotOptions _options;
    private readonly ILogger<ChatService> _logger;

    public ChatService(
        IChatClient chatClient,
        IConversationStore conversations,
        IOptions<ChatbotOptions> options,
        ILogger<ChatService> logger)
    {
        _chatClient = chatClient;
        _conversations = conversations;
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

        _logger.LogInformation(
            "Kalder model for samtale {ConversationId} med {MessageCount} beskeder.",
            conversationId,
            prompt.Count);

        var response = await _chatClient.GetResponseAsync(prompt, cancellationToken: cancellationToken);
        var reply = response.Text;

        await _conversations.AppendAsync(
            conversationId,
            [userMessage, new ChatMessage(ChatRole.Assistant, reply)],
            cancellationToken);

        return new ChatTurnResult(reply, conversationId);
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
