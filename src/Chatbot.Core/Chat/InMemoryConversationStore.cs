using System.Collections.Concurrent;
using Microsoft.Extensions.AI;

namespace Chatbot.Core.Chat;

/// <summary>
/// Chathistorik i hukommelsen — til tests og til at køre uden database. Samtaler forsvinder ved genstart.
/// </summary>
public sealed class InMemoryConversationStore : IConversationStore
{
    private sealed class Conversation(string owner)
    {
        public string Owner { get; } = owner;

        public List<ChatMessage> Messages { get; } = [];
    }

    private readonly ConcurrentDictionary<string, Conversation> _conversations = new();

    public Task<string?> GetOwnerAsync(string conversationId, CancellationToken cancellationToken = default)
        => Task.FromResult(_conversations.TryGetValue(conversationId, out var c) ? c.Owner : null);

    public Task<IReadOnlyList<ChatMessage>> GetAsync(string conversationId, int maxMessages = 0, CancellationToken cancellationToken = default)
    {
        if (!_conversations.TryGetValue(conversationId, out var conversation))
        {
            return Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        }

        lock (conversation.Messages)
        {
            IEnumerable<ChatMessage> messages = conversation.Messages;
            if (maxMessages > 0 && conversation.Messages.Count > maxMessages)
            {
                messages = conversation.Messages.Skip(conversation.Messages.Count - maxMessages);
            }

            return Task.FromResult<IReadOnlyList<ChatMessage>>(messages.ToArray());
        }
    }

    public Task AppendAsync(string conversationId, string userId, IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        var conversation = _conversations.GetOrAdd(conversationId, _ => new Conversation(userId));

        lock (conversation.Messages)
        {
            conversation.Messages.AddRange(messages);
        }

        return Task.CompletedTask;
    }
}
