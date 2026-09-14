using Microsoft.Extensions.AI;

namespace Chatbot.Core.Chat;

/// <summary>
/// Gemmer chathistorikken pr. samtale. In-memory i fase 1-4; fra fase 5.2 i Postgres, så samtaler
/// overlever genstart. En samtale har en ejer: kun den bruger, der startede den, kan fortsætte den
/// — ellers kunne én bruger sige "ja" til en anden brugers ventende handling.
/// </summary>
public interface IConversationStore
{
    /// <summary>Ejeren af samtalen (bruger-id), eller null hvis samtalen ikke findes.</summary>
    Task<string?> GetOwnerAsync(string conversationId, CancellationToken cancellationToken = default);

    /// <summary>Samtalens seneste beskeder i kronologisk rækkefølge (højst <paramref name="maxMessages"/>, 0 = alle). Tom liste hvis samtalen er ny.</summary>
    Task<IReadOnlyList<ChatMessage>> GetAsync(string conversationId, int maxMessages = 0, CancellationToken cancellationToken = default);

    /// <summary>Tilføjer beskeder til samtalen og opretter den med <paramref name="userId"/> som ejer, hvis den ikke findes.</summary>
    Task AppendAsync(string conversationId, string userId, IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default);
}
