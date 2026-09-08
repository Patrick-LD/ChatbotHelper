namespace Chatbot.Core.Chat;

/// <summary>
/// Orkestreringen af én tur i samtalen. I fase 1 er det systemprompt + historik + modelkald;
/// senere kommer RAG-søgning og tools ind samme sted.
/// </summary>
public interface IChatService
{
    Task<ChatTurnResult> SendAsync(ChatTurnRequest request, CancellationToken cancellationToken = default);
}

/// <param name="Message">Brugerens besked.</param>
/// <param name="ConversationId">Samtalen der fortsættes. Er den tom, startes en ny.</param>
public sealed record ChatTurnRequest(string Message, string? ConversationId = null);

/// <param name="Reply">Modellens svar.</param>
/// <param name="ConversationId">Id'et der skal sendes med i næste kald for at bevare konteksten.</param>
public sealed record ChatTurnResult(string Reply, string ConversationId);
