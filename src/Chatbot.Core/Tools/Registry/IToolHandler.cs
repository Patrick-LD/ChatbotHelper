using System.Text.Json;

namespace Chatbot.Core.Tools.Registry;

/// <summary>
/// Udfører et tool af én bestemt <see cref="ToolHandlerType"/>. Runtime-loaderen slår handleren
/// op ud fra tool-definitionen og giver den argumenterne fra modellen som et JSON-objekt.
///
/// En handler må ikke kaste for forventelige fejl (systemet afviste, kunne ikke nås): den returnerer
/// en tekst, modellen kan forklare brugeren. Det er begyndelsen på fase 5.2's "botten skal forklare, hvad der gik galt".
/// </summary>
public interface IToolHandler
{
    ToolHandlerType HandlerType { get; }

    /// <summary>Kontrollerer handler-konfigurationen, før en definition gemmes. Returnerer fejlbeskeder — tom liste = ok.</summary>
    IReadOnlyList<string> Validate(JsonElement handlerConfig);

    Task<string> InvokeAsync(ToolDefinition tool, JsonElement arguments, CancellationToken cancellationToken = default);
}

/// <summary>
/// Et tool implementeret i koden, som registret kan pege på med handler-typen <c>internal</c>
/// og konfigurationen <c>{ "handler": "&lt;Key&gt;" }</c>. Fase 2's dokumentationssøgning er det første.
/// Registret bestemmer stadig navn, beskrivelse, skema og roller — koden leverer kun udførelsen.
/// </summary>
public interface IInternalTool
{
    string Key { get; }

    Task<string> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken = default);
}
