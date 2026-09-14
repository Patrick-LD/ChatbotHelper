namespace Chatbot.Core.Chat;

/// <summary>
/// Oplysninger om den aktuelle tur, som tools har brug for, men som modellen ikke skal styre:
/// hvilken samtale er vi i, og hvilke roller har brugeren? Sættes af <see cref="ChatService"/> før
/// modelkaldet og lever ét request (scoped). Et tool, der forberder en handling, gemmer den under
/// samtalens id herfra; tool-registret filtrerer på rollerne.
/// </summary>
public sealed class TurnContext
{
    public string ConversationId { get; set; } = string.Empty;

    /// <summary>
    /// Brugerens roller i denne tur. Indtil fase 5.1 kobler rigtig autentificering på, kommer de fra
    /// <c>X-Roles</c>-headeren eller <c>Tools:DefaultRoles</c> — dvs. de er en påstand, ikke et bevis.
    /// </summary>
    public IReadOnlyList<string> Roles { get; set; } = [];
}
