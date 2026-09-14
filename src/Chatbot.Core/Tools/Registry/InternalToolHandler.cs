using System.Text.Json;

namespace Chatbot.Core.Tools.Registry;

/// <summary>
/// Handler for tool-typen <c>internal</c>: slår en <see cref="IInternalTool"/> op på nøglen i
/// <c>{ "handler": "…" }</c> og giver den argumenterne. Så kan dokumentationssøgningen (og senere
/// andre tools i koden) styres fra registret på lige fod med HTTP- og MCP-tools: navn, beskrivelse,
/// roller og aktiv/inaktiv ligger i databasen — kun udførelsen er kode.
/// </summary>
public sealed class InternalToolHandler : IToolHandler
{
    private readonly IReadOnlyDictionary<string, IInternalTool> _tools;

    public InternalToolHandler(IEnumerable<IInternalTool> tools)
        => _tools = tools.ToDictionary(t => t.Key, StringComparer.OrdinalIgnoreCase);

    public ToolHandlerType HandlerType => ToolHandlerType.Internal;

    public IReadOnlyList<string> AvailableKeys => _tools.Keys.OrderBy(k => k).ToList();

    public IReadOnlyList<string> Validate(JsonElement handlerConfig)
    {
        var key = ReadKey(handlerConfig);
        if (key is null)
        {
            return [$"handlerConfig.handler mangler. Tilgængelige interne handlers: {string.Join(", ", AvailableKeys)}."];
        }

        return _tools.ContainsKey(key)
            ? []
            : [$"Der findes ingen intern handler med nøglen '{key}'. Tilgængelige: {string.Join(", ", AvailableKeys)}."];
    }

    public Task<string> InvokeAsync(ToolDefinition tool, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var key = ReadKey(tool.HandlerConfig);
        if (key is null || !_tools.TryGetValue(key, out var internalTool))
        {
            return Task.FromResult(
                $"Toolet '{tool.Name}' peger på en intern handler ('{key}'), der ikke findes i denne version af chatbotten. " +
                "Fortæl brugeren, at funktionen ikke er tilgængelig lige nu.");
        }

        return internalTool.InvokeAsync(arguments, cancellationToken);
    }

    private static string? ReadKey(JsonElement config)
        => config.ValueKind == JsonValueKind.Object
           && config.TryGetProperty("handler", out var handler)
           && handler.ValueKind == JsonValueKind.String
           && !string.IsNullOrWhiteSpace(handler.GetString())
            ? handler.GetString()
            : null;
}
