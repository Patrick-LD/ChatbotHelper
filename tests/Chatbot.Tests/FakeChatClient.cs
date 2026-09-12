using Microsoft.Extensions.AI;

namespace Chatbot.Tests;

/// <summary>
/// Står i stedet for Ollama i testene: svarer altid det samme og gemmer den prompt, den fik.
/// </summary>
internal sealed class FakeChatClient : IChatClient
{
    private readonly string _reply;

    public FakeChatClient(string reply = "Det er noteret.") => _reply = reply;

    /// <summary>Prompten fra seneste kald — dét testene kigger på.</summary>
    public IReadOnlyList<ChatMessage> LastPrompt { get; private set; } = [];

    /// <summary>Options fra seneste kald — her ligger de tools, modellen fik at vælge imellem.</summary>
    public ChatOptions? LastOptions { get; private set; }

    public int CallCount { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        LastPrompt = messages.ToArray();
        LastOptions = options;
        CallCount++;
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _reply)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
