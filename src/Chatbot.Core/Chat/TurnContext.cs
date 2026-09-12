namespace Chatbot.Core.Chat;

/// <summary>
/// Oplysninger om den aktuelle tur, som tools har brug for, men som modellen ikke skal styre:
/// hvilken samtale er vi i? Sættes af <see cref="ChatService"/> før modelkaldet og lever ét request
/// (scoped). Et tool, der forbereder en handling, gemmer den under samtalens id herfra.
/// </summary>
public sealed class TurnContext
{
    public string ConversationId { get; set; } = string.Empty;
}
