using Microsoft.Extensions.AI;

namespace Chatbot.Core.Chat;

/// <summary>
/// Gemmer chathistorikken pr. samtale. Implementeres in-memory i fase 1 og
/// flyttes til databasen i fase 5.2.
/// </summary>
public interface IConversationStore
{
    /// <summary>Henter samtalens beskeder i kronologisk rækkefølge. Tom liste hvis samtalen er ny.</summary>
    Task<IReadOnlyList<ChatMessage>> GetAsync(string conversationId, CancellationToken cancellationToken = default);

    /// <summary>Tilføjer beskeder til samtalen og opretter den, hvis den ikke findes.</summary>
    Task AppendAsync(string conversationId, IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default);
}
