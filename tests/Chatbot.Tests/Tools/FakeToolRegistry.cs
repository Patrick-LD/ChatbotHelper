using System.Text.Json;
using Chatbot.Core.Tools.Registry;

namespace Chatbot.Tests.Tools;

/// <summary>Tool-registret i hukommelsen — samme rettighedsregel som SQL'en i PgToolRegistry.</summary>
internal sealed class FakeToolRegistry : IToolRegistry
{
    public List<ToolDefinition> Tools { get; } = [];

    public List<RoleDefinition> Roles { get; } = [];

    public List<McpServerDefinition> Servers { get; } = [];

    /// <summary>Sættes for at simulere, at databasen er nede.</summary>
    public Exception? FailWith { get; set; }

    public Task EnsureCreatedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<ToolDefinition>> GetForRolesAsync(IReadOnlyCollection<string> roles, CancellationToken cancellationToken = default)
    {
        if (FailWith is not null) throw FailWith;
        IReadOnlyList<ToolDefinition> result = Tools
            .Where(t => t.IsActive && t.Roles.Any(r => roles.Contains(r, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<ToolDefinition>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ToolDefinition>>(Tools.OrderBy(t => t.Name).ToList());

    public Task<ToolDefinition?> GetAsync(string name, CancellationToken cancellationToken = default)
        => Task.FromResult(Tools.FirstOrDefault(t => t.Name == name));

    public Task UpsertAsync(ToolDefinition tool, CancellationToken cancellationToken = default)
    {
        Tools.RemoveAll(t => t.Name == tool.Name);
        Tools.Add(tool);
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default)
        => Task.FromResult(Tools.RemoveAll(t => t.Name == name) > 0);

    public Task<long> CountAsync(CancellationToken cancellationToken = default) => Task.FromResult((long)Tools.Count);

    public Task<IReadOnlyList<RoleDefinition>> ListRolesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<RoleDefinition>>(Roles.ToList());

    public Task UpsertRoleAsync(RoleDefinition role, CancellationToken cancellationToken = default)
    {
        Roles.RemoveAll(r => r.Name == role.Name);
        Roles.Add(role);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<McpServerDefinition>> ListMcpServersAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<McpServerDefinition>>(Servers.ToList());

    public Task<McpServerDefinition?> GetMcpServerAsync(string name, CancellationToken cancellationToken = default)
        => Task.FromResult(Servers.FirstOrDefault(s => s.Name == name));

    public Task UpsertMcpServerAsync(McpServerDefinition server, CancellationToken cancellationToken = default)
    {
        Servers.RemoveAll(s => s.Name == server.Name);
        Servers.Add(server);
        return Task.CompletedTask;
    }

    public Task<bool> DeleteMcpServerAsync(string name, CancellationToken cancellationToken = default)
        => Task.FromResult(Servers.RemoveAll(s => s.Name == name) > 0);
}

/// <summary>Registrerer kald og svarer med fast tekst — står i stedet for HTTP/MCP/interne handlers.</summary>
internal class FakeToolHandlerBase(ToolHandlerType type = ToolHandlerType.Http, string reply = "handler-svar") : IToolHandler
{
    public ToolHandlerType HandlerType => type;

    public List<(string Tool, JsonElement Arguments)> Calls { get; } = [];

    public IReadOnlyList<string> Validate(JsonElement handlerConfig) => [];

    public virtual Task<string> InvokeAsync(ToolDefinition tool, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        Calls.Add((tool.Name, arguments.Clone()));
        return Task.FromResult(reply);
    }
}

internal sealed class FakeToolHandler(ToolHandlerType type = ToolHandlerType.Http, string reply = "handler-svar")
    : FakeToolHandlerBase(type, reply);

internal static class TestJson
{
    public static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static ToolDefinition Tool(
        string name,
        bool requiresConfirmation = false,
        string? summary = null,
        bool isActive = true,
        ToolHandlerType type = ToolHandlerType.Http,
        params string[] roles) => new(
        Name: name,
        Description: $"Beskrivelse af {name}",
        ParametersSchema: Parse("""
            {
              "type": "object",
              "properties": {
                "navn":  { "type": "string" },
                "email": { "type": "string" }
              },
              "required": ["navn", "email"]
            }
            """),
        HandlerType: type,
        HandlerConfig: Parse("{}"),
        RequiresConfirmation: requiresConfirmation,
        SummaryTemplate: summary,
        IsActive: isActive,
        Roles: roles);
}
