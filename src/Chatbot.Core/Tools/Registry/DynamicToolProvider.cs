using Chatbot.Core.Actions;
using Chatbot.Core.Chat;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Chatbot.Core.Tools.Registry;

/// <summary>
/// Fase 4's <see cref="IChatToolProvider"/>: læser tool-definitionerne fra registret ved hver tur og
/// bygger en <see cref="RegistryFunction"/> pr. række. Der er ingen cache — én forespørgsel pr. tur er
/// billig, og det er netop dét, der gør, at en ny række virker uden genstart.
///
/// Rettighederne håndhæves her og ikke i prompten: modellen får kun de tools, brugerens roller giver
/// adgang til, og kan derfor hverken se eller kalde resten. Det er bevidst "kan ikke" frem for "må ikke".
/// </summary>
public sealed class DynamicToolProvider : IChatToolProvider
{
    private readonly IToolRegistry _registry;
    private readonly IReadOnlyDictionary<ToolHandlerType, IToolHandler> _handlers;
    private readonly IPendingActionStore _pending;
    private readonly TurnContext _turn;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<DynamicToolProvider> _logger;

    public DynamicToolProvider(
        IToolRegistry registry,
        IEnumerable<IToolHandler> handlers,
        IPendingActionStore pending,
        TurnContext turn,
        ILoggerFactory loggerFactory)
    {
        _registry = registry;
        _handlers = handlers.ToDictionary(h => h.HandlerType);
        _pending = pending;
        _turn = turn;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<DynamicToolProvider>();
    }

    public async Task<IReadOnlyList<AITool>> GetToolsAsync(CancellationToken cancellationToken = default)
    {
        if (_turn.Roles.Count == 0)
        {
            _logger.LogInformation("Turen har ingen roller — modellen får ingen tools.");
            return [];
        }

        IReadOnlyList<ToolDefinition> definitions;
        try
        {
            definitions = await _registry.GetForRolesAsync(_turn.Roles, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Samme holdning som vektor-databasen i fase 2: er databasen nede, virker chatten stadig —
            // bare uden tools — og fejlen står tydeligt i loggen.
            _logger.LogError(ex, "Kunne ikke læse tool-registret. Kører Postgres? Modellen får ingen tools i denne tur.");
            return [];
        }

        var tools = new List<AITool>(definitions.Count);
        foreach (var definition in definitions)
        {
            if (!_handlers.TryGetValue(definition.HandlerType, out var handler))
            {
                _logger.LogWarning(
                    "Toolet {Tool} har handler-typen {HandlerType}, som ingen registreret handler understøtter. Springes over.",
                    definition.Name, definition.HandlerType);
                continue;
            }

            tools.Add(new RegistryFunction(definition, handler, _pending, _turn, _loggerFactory.CreateLogger<RegistryFunction>()));
        }

        _logger.LogInformation(
            "Roller [{Roles}] giver {ToolCount} tools: {Tools}",
            string.Join(", ", _turn.Roles),
            tools.Count,
            string.Join(", ", tools.Select(t => t.Name)));

        return tools;
    }
}
