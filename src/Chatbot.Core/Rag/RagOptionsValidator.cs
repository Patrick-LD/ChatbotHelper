using Microsoft.Extensions.Options;

namespace Chatbot.Core.Rag;

/// <summary>Source-genereret validering af <see cref="RagOptions"/> — samme mønster som for ChatbotOptions.</summary>
[OptionsValidator]
public sealed partial class RagOptionsValidator : IValidateOptions<RagOptions>;
