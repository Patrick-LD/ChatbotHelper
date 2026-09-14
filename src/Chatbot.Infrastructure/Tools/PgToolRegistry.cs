using System.Text.Json;
using Chatbot.Core.Tools;
using Chatbot.Core.Tools.Registry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Chatbot.Infrastructure.Tools;

/// <summary>
/// Tool-registret i Postgres (fase 4.1). Fire tabeller:
/// <list type="bullet">
/// <item><c>tools</c> — navn, beskrivelse, JSON-skema, handler-type/-konfiguration, bekræftelse, aktiv.</item>
/// <item><c>roles</c> og <c>tool_roles</c> — hvem må bruge hvad. Rettighedskoblingen er en tabel, ikke kode.</item>
/// <item><c>mcp_servers</c> — MCP-servere, hvis tools kan importeres til <c>tools</c>.</item>
/// </list>
/// Rå SQL med Npgsql ligesom vektor-lageret: fire tabeller retfærdiggør ikke en ORM, og SQL'en her
/// er samtidig dokumentationen af skemaet.
/// </summary>
public sealed class PgToolRegistry : IToolRegistry, IAsyncDisposable
{
    private const string ToolColumns = """
        t.name, t.description, t.parameters_schema::text, t.handler_type, t.handler_config::text,
        t.requires_confirmation, t.summary_template, t.is_active, t.updated_at,
        COALESCE((SELECT array_agg(role_name ORDER BY role_name) FROM tool_roles WHERE tool_name = t.name), '{}'::text[]) AS roles
        """;

    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PgToolRegistry> _logger;

    public PgToolRegistry(IOptions<ToolsOptions> options, ILogger<PgToolRegistry> logger)
    {
        _dataSource = NpgsqlDataSource.Create(options.Value.Registry.ConnectionString);
        _logger = logger;
    }

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS roles (
                name        text PRIMARY KEY,
                description text NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS tools (
                name                  text PRIMARY KEY,
                description           text NOT NULL,
                parameters_schema     jsonb NOT NULL,
                handler_type          text NOT NULL CHECK (handler_type IN ('internal', 'http', 'mcp')),
                handler_config        jsonb NOT NULL DEFAULT '{}'::jsonb,
                requires_confirmation boolean NOT NULL DEFAULT false,
                summary_template      text,
                is_active             boolean NOT NULL DEFAULT true,
                updated_at            timestamptz NOT NULL DEFAULT now()
            );

            CREATE TABLE IF NOT EXISTS tool_roles (
                tool_name text NOT NULL REFERENCES tools(name) ON DELETE CASCADE,
                role_name text NOT NULL REFERENCES roles(name) ON DELETE CASCADE,
                PRIMARY KEY (tool_name, role_name)
            );

            CREATE TABLE IF NOT EXISTS mcp_servers (
                name       text PRIMARY KEY,
                transport  text NOT NULL CHECK (transport IN ('stdio', 'http')),
                command    text,
                arguments  text[] NOT NULL DEFAULT '{}'::text[],
                url        text,
                is_active  boolean NOT NULL DEFAULT true,
                updated_at timestamptz NOT NULL DEFAULT now()
            );
            """;

        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogDebug("Tool-registrets tabeller er klar.");
    }

    public async Task<IReadOnlyList<ToolDefinition>> GetForRolesAsync(IReadOnlyCollection<string> roles, CancellationToken cancellationToken = default)
    {
        if (roles.Count == 0)
        {
            return [];
        }

        // Rettighedsfilteret ligger i SQL'en: et tool uden rækker i tool_roles matcher aldrig.
        var sql = $"""
            SELECT {ToolColumns}
            FROM tools t
            WHERE t.is_active
              AND EXISTS (SELECT 1 FROM tool_roles tr WHERE tr.tool_name = t.name AND tr.role_name = ANY($1))
            ORDER BY t.name
            """;

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.Add(new() { Value = roles.ToArray(), NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text });
        return await ReadToolsAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<ToolDefinition>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand($"SELECT {ToolColumns} FROM tools t ORDER BY t.name");
        return await ReadToolsAsync(command, cancellationToken);
    }

    public async Task<ToolDefinition?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand($"SELECT {ToolColumns} FROM tools t WHERE t.name = $1");
        command.Parameters.Add(new() { Value = name });
        var tools = await ReadToolsAsync(command, cancellationToken);
        return tools.Count == 0 ? null : tools[0];
    }

    public async Task UpsertAsync(ToolDefinition tool, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        const string upsertTool = """
            INSERT INTO tools (name, description, parameters_schema, handler_type, handler_config, requires_confirmation, summary_template, is_active, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, now())
            ON CONFLICT (name) DO UPDATE SET
                description           = EXCLUDED.description,
                parameters_schema     = EXCLUDED.parameters_schema,
                handler_type          = EXCLUDED.handler_type,
                handler_config        = EXCLUDED.handler_config,
                requires_confirmation = EXCLUDED.requires_confirmation,
                summary_template      = EXCLUDED.summary_template,
                is_active             = EXCLUDED.is_active,
                updated_at            = now()
            """;

        await using (var command = new NpgsqlCommand(upsertTool, connection, transaction))
        {
            command.Parameters.Add(new() { Value = tool.Name });
            command.Parameters.Add(new() { Value = tool.Description });
            command.Parameters.Add(new() { Value = tool.ParametersSchema.GetRawText(), NpgsqlDbType = NpgsqlDbType.Jsonb });
            command.Parameters.Add(new() { Value = tool.HandlerType.ToString().ToLowerInvariant() });
            command.Parameters.Add(new() { Value = tool.HandlerConfig.GetRawText(), NpgsqlDbType = NpgsqlDbType.Jsonb });
            command.Parameters.Add(new() { Value = tool.RequiresConfirmation });
            command.Parameters.Add(new() { Value = (object?)tool.SummaryTemplate ?? DBNull.Value });
            command.Parameters.Add(new() { Value = tool.IsActive });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var command = new NpgsqlCommand("DELETE FROM tool_roles WHERE tool_name = $1", connection, transaction))
        {
            command.Parameters.Add(new() { Value = tool.Name });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var role in tool.Roles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await using (var command = new NpgsqlCommand("INSERT INTO roles (name) VALUES ($1) ON CONFLICT DO NOTHING", connection, transaction))
            {
                command.Parameters.Add(new() { Value = role });
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var command = new NpgsqlCommand("INSERT INTO tool_roles (tool_name, role_name) VALUES ($1, $2) ON CONFLICT DO NOTHING", connection, transaction))
            {
                command.Parameters.Add(new() { Value = tool.Name });
                command.Parameters.Add(new() { Value = role });
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand("DELETE FROM tools WHERE name = $1");
        command.Parameters.Add(new() { Value = name });
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand("SELECT count(*) FROM tools");
        return await command.ExecuteScalarAsync(cancellationToken) is long count ? count : 0;
    }

    public async Task<IReadOnlyList<RoleDefinition>> ListRolesAsync(CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand("SELECT name, description FROM roles ORDER BY name");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var roles = new List<RoleDefinition>();
        while (await reader.ReadAsync(cancellationToken))
        {
            roles.Add(new RoleDefinition(reader.GetString(0), reader.GetString(1)));
        }

        return roles;
    }

    public async Task UpsertRoleAsync(RoleDefinition role, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand(
            "INSERT INTO roles (name, description) VALUES ($1, $2) ON CONFLICT (name) DO UPDATE SET description = EXCLUDED.description");
        command.Parameters.Add(new() { Value = role.Name });
        command.Parameters.Add(new() { Value = role.Description });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<McpServerDefinition>> ListMcpServersAsync(CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand(
            "SELECT name, transport, command, arguments, url, is_active, updated_at FROM mcp_servers ORDER BY name");
        return await ReadServersAsync(command, cancellationToken);
    }

    public async Task<McpServerDefinition?> GetMcpServerAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand(
            "SELECT name, transport, command, arguments, url, is_active, updated_at FROM mcp_servers WHERE name = $1");
        command.Parameters.Add(new() { Value = name });
        var servers = await ReadServersAsync(command, cancellationToken);
        return servers.Count == 0 ? null : servers[0];
    }

    public async Task UpsertMcpServerAsync(McpServerDefinition server, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO mcp_servers (name, transport, command, arguments, url, is_active, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6, now())
            ON CONFLICT (name) DO UPDATE SET
                transport  = EXCLUDED.transport,
                command    = EXCLUDED.command,
                arguments  = EXCLUDED.arguments,
                url        = EXCLUDED.url,
                is_active  = EXCLUDED.is_active,
                updated_at = now()
            """;

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.Add(new() { Value = server.Name });
        command.Parameters.Add(new() { Value = server.Transport.ToString().ToLowerInvariant() });
        command.Parameters.Add(new() { Value = (object?)server.Command ?? DBNull.Value });
        command.Parameters.Add(new() { Value = server.Arguments.ToArray(), NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text });
        command.Parameters.Add(new() { Value = (object?)server.Url ?? DBNull.Value });
        command.Parameters.Add(new() { Value = server.IsActive });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> DeleteMcpServerAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand("DELETE FROM mcp_servers WHERE name = $1");
        command.Parameters.Add(new() { Value = name });
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    private static async Task<IReadOnlyList<ToolDefinition>> ReadToolsAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var tools = new List<ToolDefinition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tools.Add(new ToolDefinition(
                Name: reader.GetString(0),
                Description: reader.GetString(1),
                ParametersSchema: ParseJson(reader.GetString(2)),
                HandlerType: Enum.Parse<ToolHandlerType>(reader.GetString(3), ignoreCase: true),
                HandlerConfig: ParseJson(reader.GetString(4)),
                RequiresConfirmation: reader.GetBoolean(5),
                SummaryTemplate: reader.IsDBNull(6) ? null : reader.GetString(6),
                IsActive: reader.GetBoolean(7),
                UpdatedAt: reader.GetFieldValue<DateTime>(8).ToUniversalTime(),
                Roles: reader.GetFieldValue<string[]>(9)));
        }

        return tools;
    }

    private static async Task<IReadOnlyList<McpServerDefinition>> ReadServersAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var servers = new List<McpServerDefinition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            servers.Add(new McpServerDefinition(
                Name: reader.GetString(0),
                Transport: Enum.Parse<McpTransport>(reader.GetString(1), ignoreCase: true),
                Command: reader.IsDBNull(2) ? null : reader.GetString(2),
                Arguments: reader.GetFieldValue<string[]>(3),
                Url: reader.IsDBNull(4) ? null : reader.GetString(4),
                IsActive: reader.GetBoolean(5),
                UpdatedAt: reader.GetFieldValue<DateTime>(6).ToUniversalTime()));
        }

        return servers;
    }

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
