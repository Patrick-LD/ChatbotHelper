namespace Chatbot.Core.Actions;

/// <summary>
/// En handling, botten har forberedt, men som venter på brugerens bekræftelse.
/// Det er kernen i fase 3's to-trins-flow: modellen må *foreslå* en handling med udfyldte
/// parametre, men den udføres først, når brugeren har sagt ja — og det "ja" afgøres af
/// orkestratoren, ikke af modellen. Sikkerheden må ikke afhænge af modellens lydighed.
/// </summary>
/// <param name="ToolName">Toolet der skal udføre handlingen, f.eks. "opret_medarbejder".</param>
/// <param name="Summary">Menneskelæsbar opsummering: "Opret Lars Hansen (lars@firma.dk) i Salg fra 1. oktober".</param>
/// <param name="ParametersJson">Parametrene som JSON, så handlingen kan udføres uden et nyt modelkald.</param>
/// <param name="CreatedAt">Hvornår den blev forberedt. Bruges til at lade gamle forslag udløbe.</param>
public sealed record PendingAction(string ToolName, string Summary, string ParametersJson, DateTimeOffset CreatedAt);

/// <summary>Gemmer højst én ventende handling pr. samtale. Et nyt forslag erstatter det gamle.</summary>
public interface IPendingActionStore
{
    Task<PendingAction?> GetAsync(string conversationId, CancellationToken cancellationToken = default);

    Task SetAsync(string conversationId, PendingAction action, CancellationToken cancellationToken = default);

    Task ClearAsync(string conversationId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Udfører en bekræftet handling. Orkestratoren spørger de registrerede executors, hvem der kan
/// håndtere den ventende handlings tool. Fra fase 4 er der én generisk executor for alle registry-tools;
/// interfacet er bevaret, så et særligt tool stadig kan have sin egen.
/// </summary>
public interface IActionExecutor
{
    Task<bool> CanExecuteAsync(string toolName, CancellationToken cancellationToken = default);

    /// <summary>Udfører handlingen og returnerer en besked til brugeren om resultatet.</summary>
    Task<string> ExecuteAsync(PendingAction action, CancellationToken cancellationToken = default);
}
