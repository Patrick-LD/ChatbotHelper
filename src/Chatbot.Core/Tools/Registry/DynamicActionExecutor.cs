using System.Diagnostics;
using System.Text.Json;
using Chatbot.Core.Actions;
using Chatbot.Core.Chat;
using Chatbot.Core.Security;
using Microsoft.Extensions.Logging;

namespace Chatbot.Core.Tools.Registry;

/// <summary>
/// Udfører en bekræftet handling for et registry-tool. Kaldes af <see cref="ChatService"/>, når
/// brugeren har sagt ja — uden modelkald, med de parametre der blev gemt, da forslaget blev lavet.
///
/// Rettighederne tjekkes igen her: toolet skal stadig være aktivt og tilladt for turens roller.
/// Et forslag fra i går må ikke kunne udføres, hvis toolet siden er deaktiveret eller rettigheden fjernet.
/// Udfaldet (udført, afvist, fejlet) skrives i audit-loggen med bruger og parametre (fase 5.1).
/// </summary>
public sealed class DynamicActionExecutor : IActionExecutor
{
    private readonly IToolRegistry _registry;
    private readonly IReadOnlyDictionary<ToolHandlerType, IToolHandler> _handlers;
    private readonly TurnContext _turn;
    private readonly IAuditLog _audit;
    private readonly ILogger<DynamicActionExecutor> _logger;

    public DynamicActionExecutor(
        IToolRegistry registry,
        IEnumerable<IToolHandler> handlers,
        TurnContext turn,
        IAuditLog audit,
        ILogger<DynamicActionExecutor> logger)
    {
        _registry = registry;
        _handlers = handlers.ToDictionary(h => h.HandlerType);
        _turn = turn;
        _audit = audit;
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
            const string denied = "Handlingen blev ikke udført: toolet er ikke længere tilgængeligt for dig " +
                                  "(det er fjernet, deaktiveret eller uden for dine rettigheder). Intet er ændret.";
            _logger.LogWarning("Handling afvist i samtale {ConversationId}: {Tool} er ikke tilgængelig for [{Roles}].", _turn.ConversationId, action.ToolName, string.Join(", ", _turn.Roles));
            await AuditAsync(action, AuditKind.Denied, denied, success: false, 0, cancellationToken);
            return denied;
        }

        if (!_handlers.TryGetValue(tool.HandlerType, out var handler))
        {
            var message = $"Handlingen blev ikke udført: der er ingen handler for tool-typen {tool.HandlerType}. Intet er ændret.";
            await AuditAsync(action, AuditKind.Failed, message, success: false, 0, cancellationToken);
            return message;
        }

        using var document = JsonDocument.Parse(action.ParametersJson);
        var arguments = document.RootElement.Clone();

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await handler.InvokeAsync(tool, arguments, cancellationToken);
            stopwatch.Stop();
            _logger.LogInformation("Handling udført i samtale {ConversationId}: {Tool} → {Result}", _turn.ConversationId, tool.Name, result);
            await AuditAsync(action, AuditKind.Executed, result, success: true, stopwatch.ElapsedMilliseconds, cancellationToken);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Handling fejlede uventet i samtale {ConversationId}: {Tool}", _turn.ConversationId, tool.Name);
            await AuditAsync(action, AuditKind.Failed, ex.Message, success: false, stopwatch.ElapsedMilliseconds, cancellationToken);
            return $"Handlingen kunne ikke udføres: systemet bag '{tool.Name}' fejlede uventet, og fejlen er logget. " +
                   "Det er uvist, om noget blev ændret — tjek i systemet, før du prøver igen.";
        }
    }

    private async Task AuditAsync(PendingAction action, string kind, string result, bool success, long durationMs, CancellationToken cancellationToken)
    {
        try
        {
            await _audit.WriteAsync(
                new AuditEntry(DateTimeOffset.UtcNow, _turn.UserId, _turn.Roles, _turn.ConversationId, action.ToolName, kind, action.ParametersJson, result, success, durationMs),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Audit-loggen kunne ikke skrives for handlingen {Tool}.", action.ToolName);
        }
    }
}
