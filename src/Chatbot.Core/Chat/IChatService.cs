namespace Chatbot.Core.Chat;

/// <summary>
/// Orkestreringen af én tur i samtalen: systemprompt + historik + tools → modelkald,
/// samt bekræftelses-flowet for handlinger, der venter på brugerens ja.
/// </summary>
public interface IChatService
{
    Task<ChatTurnResult> SendAsync(ChatTurnRequest request, CancellationToken cancellationToken = default);
}

/// <param name="Message">Brugerens besked.</param>
/// <param name="ConversationId">Samtalen der fortsættes. Er den tom, startes en ny.</param>
public sealed record ChatTurnRequest(string Message, string? ConversationId = null);

/// <param name="Reply">Botten svar.</param>
/// <param name="ConversationId">Id'et der skal sendes med i næste kald for at bevare konteksten.</param>
/// <param name="Sources">Dokumentationsuddrag, botten slog op i denne tur. Tom, hvis den ikke søgte.</param>
/// <param name="PendingAction">Opsummering af en handling, der venter på bekræftelse. Null, hvis ingen venter.</param>
public sealed record ChatTurnResult(
    string Reply,
    string ConversationId,
    IReadOnlyList<SourceReference> Sources,
    string? PendingAction = null);

/// <param name="Source">Kildedokument, f.eks. "personalehaandbog.md".</param>
/// <param name="Heading">Afsnittet i dokumentet.</param>
/// <param name="Score">Højeste lighed (0-1) blandt de hentede chunks fra afsnittet.</param>
public sealed record SourceReference(string Source, string Heading, double Score);
