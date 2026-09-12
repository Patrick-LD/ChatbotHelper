using System.Collections.Concurrent;

namespace Chatbot.Core.Actions;

/// <summary>Ventende handlinger i hukommelsen — følger chathistorikken til databasen i fase 5.</summary>
public sealed class InMemoryPendingActionStore : IPendingActionStore
{
    private readonly ConcurrentDictionary<string, PendingAction> _actions = new();

    public Task<PendingAction?> GetAsync(string conversationId, CancellationToken cancellationToken = default)
        => Task.FromResult(_actions.TryGetValue(conversationId, out var action) ? action : null);

    public Task SetAsync(string conversationId, PendingAction action, CancellationToken cancellationToken = default)
    {
        _actions[conversationId] = action;
        return Task.CompletedTask;
    }

    public Task ClearAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        _actions.TryRemove(conversationId, out _);
        return Task.CompletedTask;
    }
}
