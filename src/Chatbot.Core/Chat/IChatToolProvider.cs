using Microsoft.Extensions.AI;

namespace Chatbot.Core.Chat;

/// <summary>
/// Leverer de tools, modellen må se i en samtale. I fase 2 var der ét hardcodet (dokumentationssøgning),
/// i fase 3 kom to handlings-tools til, og fra fase 4 kommer de alle fra tool-registret i databasen,
/// filtreret på turens roller. Orkestreringen i ChatService er den samme — den ved ikke, hvor tools kommer fra.
/// Asynkron, fordi registret læses pr. tur.
/// </summary>
public interface IChatToolProvider
{
    Task<IReadOnlyList<AITool>> GetToolsAsync(CancellationToken cancellationToken = default);
}
