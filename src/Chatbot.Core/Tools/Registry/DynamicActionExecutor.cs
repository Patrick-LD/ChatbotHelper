using System.Text.Json;
using Chatbot.Core.Actions;
using Chatbot.Core.Chat;
using Microsoft.Extensions.Logging;

namespace Chatbot.Core.Tools.Registry;

/// <summary>
/// Udfører en bekræftet handling for et registry-tool. Kaldes af <see cref="ChatService"/>, når
/// brugeren har sagt ja — uden modelkald, med de parametre der blev gemt, da forslaget blev lavet.
///
/// Rettighederne tjekkes igen her: toolet skal stadig være aktivt og tilladt for turens roller.
/// Et forslag fra i går må ikke kunne udføres, hvis toolet siden er deaktiveret eller rettigheden fjernet.
/// </summary>
public sealed class DynamicActionExecutor : IActionExecutor
{
    private readonly IToolRegistry _registry;
    private readonly IReadOnlyDictionary<ToolHandlerType, IToolHandler> _handlers;
    private readonly TurnContext _turn;
    private readonly ILogger<DynamicActionExecutor> _logger;

    public DynamicActionExecutor(
        IToolRegistry registry,
        IEnumerable<IToolHandler> handlers,
        TurnContext turn,
        ILogger<DynamicActionExecutor> logger)
    {
        _registry = registry;
        _handlers = handlers.ToDictionary(h => h.HandlerType);
        _turn = turn;
        _logger = logger;
    }

    public async Task<bool> CanExecuteAsync(string toolName, CancellationToken cancellationToken = default)
        => await _registry.GetAsync(toolName, cancellationToken) is not null;

    public async Task<string> ExecuteAsync(PendingAction action, CancellationToken cancellationToken = default)
    {
        var allowed = await _registry.GetForRolesAsync(_turn.Roles, cancellationToken);
        var tool = allowed.FirstOrDefault(t => t.Name == action.ToolName);
        if (tool is null)
        {
            _logger.LogWarning(
                "AUDIT tool={Tool} samtale={ConversationId} roller=[{Roles}] resultat=afvist aarsag=ikke-tilgaengelig",
                action.ToolName, _turn.ConversationId, string.Join(", ", _turn.Roles));

            return $"Handlingen blev ikke udført: toolet '{action.ToolName}' er ikke længere tilgængeligt for dig " +
                   "(det er fjernet, deaktiveret eller uden for dine rettigheder). Intet er ændret.";
        }

        if (!_handlers.TryGetValue(tool.HandlerType, out var handler))
        {
            return $"Handlingen blev ikke udført: der er ingen handler for tool-typen {tool.HandlerType}. Intet er ændret.";
        }

        using var document = JsonDocument.Parse(action.ParametersJson);
        var arguments = document.RootElement.Clone();

        var result = await handler.InvokeAsync(tool, arguments, cancellationToken);

        // Audit-spor (udbygges til rigtig audit-log i fase 5.1).
        _logger.LogInformation(
            "AUDIT tool={Tool} samtale={ConversationId} roller=[{Roles}] parametre={Parameters} resultat={Result}",
            tool.Name, _turn.ConversationId, string.Join(", ", _turn.Roles), action.ParametersJson, result);

        return result;
    }
}
