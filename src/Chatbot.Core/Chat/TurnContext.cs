namespace Chatbot.Core.Chat;

/// <summary>
/// Oplysninger om den aktuelle tur, som tools har brug for, men som modellen ikke skal styre:
/// hvilken samtale er vi i, og hvem er brugeren? Sættes af <see cref="ChatService"/> før
/// modelkaldet og lever ét request (scoped). Et tool, der forbereder en handling, gemmer den under
/// samtalens id herfra; tool-registret filtrerer på rollerne; audit-loggen får bruger-id'et.
/// </summary>
public sealed class TurnContext
{
    public string ConversationId { get; set; } = string.Empty;

    /// <summary>Den autentificerede brugers id (fra <see cref="Security.CurrentUser"/>).</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Brugerens roller — fra autentificeringen, ikke fra klienten.</summary>
    public IReadOnlyList<string> Roles { get; set; } = [];
}
