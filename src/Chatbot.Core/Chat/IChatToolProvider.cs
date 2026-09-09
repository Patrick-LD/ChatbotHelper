using Microsoft.Extensions.AI;

namespace Chatbot.Core.Chat;

/// <summary>
/// Leverer de tools, modellen må se i en samtale. I fase 2 er der ét (dokumentationssøgning);
/// i fase 3 kommer hardcodede handlings-tools til, og i fase 4 erstattes de af et registry i
/// databasen, der filtrerer på brugerens rettigheder. Orkestreringen i ChatService er den samme.
/// </summary>
public interface IChatToolProvider
{
    IReadOnlyList<AITool> GetTools();
}
