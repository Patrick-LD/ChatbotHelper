using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Chatbot.Core.Tools;
using Chatbot.Core.Tools.Registry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Chatbot.Infrastructure.Tools;

/// <summary>
/// Handler for tool-typen <c>mcp</c> (fase 4.3): <c>{ "server": "filsystem", "tool": "list_directory" }</c>.
/// Serveren slås op i <c>mcp_servers</c>, klienten hentes fra <see cref="McpClientPool"/>, og kaldet
/// videresendes med modellens argumenter uændret — MCP-toolets eget JSON-skema blev kopieret ind i
/// tool-rækken ved importen, så modellen udfylder præcis det, serveren forventer.
///
/// Rettigheder og bekræftelse styres af tool-rækken som for alle andre tools: en MCP-server kan
/// ikke give modellen et tool, registret ikke har sagt god for.
/// </summary>
public sealed class McpToolHandler : IToolHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly McpClientPool _pool;
    private readonly TimeSpan _timeout;
    private readonly int _maxResultChars;
    private readonly ILogger<McpToolHandler> _logger;

    public McpToolHandler(McpClientPool pool, IOptions<ToolsOptions> options, ILogger<McpToolHandler> logger)
    {
        _pool = pool;
        _timeout = TimeSpan.FromSeconds(options.Value.Registry.McpTimeoutSeconds);
        _maxResultChars = options.Value.Registry.MaxResultChars;
        _logger = logger;
    }

    public ToolHandlerType HandlerType => ToolHandlerType.Mcp;

    public IReadOnlyList<string> Validate(JsonElement handlerConfig)
    {
        var config = Read(handlerConfig);
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(config?.Server)) errors.Add("handlerConfig.server mangler (navnet på en række i mcp_servers).");
        if (string.IsNullOrWhiteSpace(config?.Tool)) errors.Add("handlerConfig.tool mangler (toolets navn på MCP-serveren).");
        return errors;
    }

    public async Task<string> InvokeAsync(ToolDefinition tool, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var config = Read(tool.HandlerConfig)
            ?? throw new InvalidOperationException($"Toolet '{tool.Name}' har ingen MCP-konfiguration.");

        var args = new Dictionary<string, object?>();
        if (arguments.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in arguments.EnumerateObject())
            {
                args[property.Name] = property.Value;
            }
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        try
        {
            var client = await _pool.GetClientAsync(config.Server, timeoutCts.Token);
            _logger.LogInformation("MCP-tool {Tool}: {Server}/{McpTool}", tool.Name, config.Server, config.Tool);

            var result = await client.CallToolAsync(config.Tool, args, cancellationToken: timeoutCts.Token);
            var text = FormatResult(result);

            return result.IsError == true
                ? $"MCP-serveren '{config.Server}' meldte fejl for '{config.Tool}': {text}"
                : text;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("MCP-tool {Tool} svarede ikke inden {Timeout}.", tool.Name, _timeout);
            _pool.Invalidate(config.Server);
            return $"MCP-serveren '{config.Server}' svarede ikke inden for {_timeout.TotalSeconds:0} sekunder. " +
                   "Fortæl brugeren, at det ikke lykkedes lige nu.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "MCP-tool {Tool} fejlede mod serveren {Server}.", tool.Name, config.Server);
            _pool.Invalidate(config.Server);
            return $"MCP-serveren '{config.Server}' kunne ikke bruges ({ex.Message}). " +
                   "Fortæl brugeren, at funktionen ikke virker lige nu.";
        }
    }

    private string FormatResult(CallToolResult result)
    {
        var sb = new StringBuilder();
        foreach (var block in result.Content)
        {
            switch (block)
            {
                case TextContentBlock textBlock:
                    sb.AppendLine(textBlock.Text);
                    break;
                case ImageContentBlock image:
                    sb.AppendLine($"[billede: {image.MimeType}]");
                    break;
                case EmbeddedResourceBlock resource:
                    sb.AppendLine(resource.Resource is TextResourceContents trc ? trc.Text : $"[ressource: {resource.Resource.Uri}]");
                    break;
                default:
                    sb.AppendLine($"[{block.Type}]");
                    break;
            }
        }

        if (sb.Length == 0 && result.StructuredContent is { } structured)
        {
            sb.Append(structured.GetRawText());
        }

        var text = sb.ToString().TrimEnd();
        if (text.Length == 0)
        {
            return "Kaldet lykkedes, men serveren returnerede intet indhold.";
        }

        return text.Length <= _maxResultChars ? text : text[.._maxResultChars] + $"… [klippet, {text.Length - _maxResultChars} tegn udeladt]";
    }

    private static McpToolConfig? Read(JsonElement config)
        => config.ValueKind == JsonValueKind.Object ? config.Deserialize<McpToolConfig>(JsonOptions) : null;

    internal sealed record McpToolConfig(string Server, string Tool);
}

/// <summary>
/// Holder én forbindelse pr. MCP-server på tværs af requests. En stdio-server er en proces, der
/// startes ved første kald og genbruges — at starte <c>npx</c> for hvert tool-kald ville koste sekunder.
/// Ændres serverens definition i registret, oprettes en ny klient; fejler et kald, kasseres den gamle.
/// </summary>
public sealed class McpClientPool : IAsyncDisposable
{
    private readonly IToolRegistry _registry;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<McpClientPool> _logger;
    private readonly ConcurrentDictionary<string, Lazy<Task<Entry>>> _clients = new();

    public McpClientPool(IToolRegistry registry, ILoggerFactory loggerFactory)
    {
        _registry = registry;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<McpClientPool>();
    }

    public async Task<McpClient> GetClientAsync(string serverName, CancellationToken cancellationToken = default)
    {
        var definition = await _registry.GetMcpServerAsync(serverName, cancellationToken)
            ?? throw new InvalidOperationException($"MCP-serveren '{serverName}' findes ikke i registret (tabellen mcp_servers).");

        if (!definition.IsActive)
        {
            throw new InvalidOperationException($"MCP-serveren '{serverName}' er deaktiveret.");
        }

        var fingerprint = Fingerprint(definition);
        var lazy = _clients.GetOrAdd(serverName, _ => new Lazy<Task<Entry>>(() => ConnectAsync(definition, fingerprint)));

        var entry = await WaitAsync(lazy, cancellationToken);
        if (entry.Fingerprint != fingerprint)
        {
            // Definitionen er ændret siden forbindelsen blev oprettet — start forfra.
            Invalidate(serverName);
            lazy = _clients.GetOrAdd(serverName, _ => new Lazy<Task<Entry>>(() => ConnectAsync(definition, fingerprint)));
            entry = await WaitAsync(lazy, cancellationToken);
        }

        return entry.Client;
    }

    /// <summary>Serverens tools, som serveren selv beskriver dem. Bruges af importen i /mcp-servers/{name}/tools.</summary>
    public async Task<IList<McpClientTool>> ListToolsAsync(string serverName, CancellationToken cancellationToken = default)
    {
        var client = await GetClientAsync(serverName, cancellationToken);
        return await client.ListToolsAsync(cancellationToken: cancellationToken);
    }

    public void Invalidate(string serverName)
    {
        if (_clients.TryRemove(serverName, out var lazy) && lazy.IsValueCreated && lazy.Value.IsCompletedSuccessfully)
        {
            _ = lazy.Value.Result.Client.DisposeAsync().AsTask().ContinueWith(
                t => _logger.LogDebug(t.Exception, "Fejl ved lukning af MCP-klient {Server}.", serverName),
                TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var name in _clients.Keys.ToList())
        {
            if (_clients.TryRemove(name, out var lazy) && lazy.IsValueCreated)
            {
                try
                {
                    var entry = await lazy.Value;
                    await entry.Client.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Fejl ved lukning af MCP-klient {Server}.", name);
                }
            }
        }
    }

    private async Task<Entry> ConnectAsync(McpServerDefinition definition, string fingerprint)
    {
        try
        {
            IClientTransport transport = definition.Transport switch
            {
                McpTransport.Stdio => new StdioClientTransport(new StdioClientTransportOptions
                {
                    Name = definition.Name,
                    Command = definition.Command ?? throw new InvalidOperationException($"MCP-serveren '{definition.Name}' mangler command."),
                    Arguments = definition.Arguments.ToList(),
                }, _loggerFactory),

                McpTransport.Http => new HttpClientTransport(new HttpClientTransportOptions
                {
                    Name = definition.Name,
                    Endpoint = new Uri(definition.Url ?? throw new InvalidOperationException($"MCP-serveren '{definition.Name}' mangler url.")),
                }, _loggerFactory),

                _ => throw new InvalidOperationException($"Ukendt transport {definition.Transport}."),
            };

            var client = await McpClient.CreateAsync(transport, loggerFactory: _loggerFactory);
            _logger.LogInformation(
                "Forbundet til MCP-serveren {Server} ({Transport}): {ServerName} {Version}",
                definition.Name, definition.Transport, client.ServerInfo.Name, client.ServerInfo.Version);

            return new Entry(client, fingerprint);
        }
        catch
        {
            _clients.TryRemove(definition.Name, out _);
            throw;
        }
    }

    private static async Task<Entry> WaitAsync(Lazy<Task<Entry>> lazy, CancellationToken cancellationToken)
        => await lazy.Value.WaitAsync(cancellationToken);

    private static string Fingerprint(McpServerDefinition d)
        => string.Join("", d.Transport, d.Command, string.Join("", d.Arguments), d.Url);

    private sealed record Entry(McpClient Client, string Fingerprint);
}
