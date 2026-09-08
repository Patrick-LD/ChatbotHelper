using System.Collections.Concurrent;
using Microsoft.Extensions.AI;

namespace Chatbot.Core.Chat;

/// <summary>
/// Chathistorik i hukommelsen — nok til fase 1. Samtaler forsvinder ved genstart.
/// </summary>
public sealed class InMemoryConversationStore : IConversationStore
{
    private readonly ConcurrentDictionary<string, List<ChatMessage>> _conversations = new();

    public Task<IReadOnlyList<ChatMessage>> GetAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        if (!_conversations.TryGetValue(conversationId, out var messages))
        {
            return Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        }

        lock (messages)
        {
            return Task.FromResult<IReadOnlyList<ChatMessage>>(messages.ToArray());
        }
    }

    public Task AppendAsync(string conversationId, IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        var conversation = _conversations.GetOrAdd(conversationId, _ => []);

        lock (conversation)
        {
            conversation.AddRange(messages);
        }

        return Task.CompletedTask;
    }
}
