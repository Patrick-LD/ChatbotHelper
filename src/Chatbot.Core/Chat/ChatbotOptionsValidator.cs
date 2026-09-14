using Microsoft.Extensions.Options;

namespace Chatbot.Core.Chat;

/// <summary>
/// Validering af <see cref="ChatbotOptions"/>. Koden bag skrives af en source generator
/// ud fra attributterne på options-klasserne — derfor <c>partial</c> og ingen krop.
/// Registreres i Program.cs, så <c>ValidateOnStart()</c> har regler at håndhæve.
/// </summary>
[OptionsValidator]
public sealed partial class ChatbotOptionsValidator : IValidateOptions<ChatbotOptions>;
