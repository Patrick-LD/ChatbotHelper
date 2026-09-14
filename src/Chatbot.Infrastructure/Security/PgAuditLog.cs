using Chatbot.Core.Security;
using Chatbot.Core.Tools;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Chatbot.Infrastructure.Security;

/// <summary>
/// Audit-loggen i Postgres (fase 5.1): hvert tool-kald, forslag, udførelse, annullering og afvisning
/// med bruger, roller, samtale, parametre, resultat og varighed. Tabellen er append-only fra kodens
/// side — der er ingen update/delete-metoder, med vilje.
/// </summary>
public sealed class PgAuditLog : IAuditLog, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public PgAuditLog(IOptions<ToolsOptions> options)
        => _dataSource = NpgsqlDataSource.Create(options.Value.Registry.ConnectionString);

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS audit_log (
                id              bigserial PRIMARY KEY,
                at              timestamptz NOT NULL,
                user_id         text NOT NULL,
                roles           text[] NOT NULL,
                conversation_id text NOT NULL,
                tool_name       text NOT NULL,
                kind            text NOT NULL,
                parameters      jsonb,
                result          text NOT NULL,
                success         boolean NOT NULL,
                duration_ms     bigint NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS audit_log_at_idx ON audit_log (at DESC);
            CREATE INDEX IF NOT EXISTS audit_log_user_idx ON audit_log (user_id, at DESC);
            """;

        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO audit_log (at, user_id, roles, conversation_id, tool_name, kind, parameters, result, success, duration_ms)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)
            """;

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.Add(new() { Value = entry.At.UtcDateTime });
        command.Parameters.Add(new() { Value = entry.UserId });
        command.Parameters.Add(new() { Value = entry.Roles.ToArray(), NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text });
        command.Parameters.Add(new() { Value = entry.ConversationId });
        command.Parameters.Add(new() { Value = entry.ToolName });
        command.Parameters.Add(new() { Value = entry.Kind });
        command.Parameters.Add(new() { Value = IsJson(entry.ParametersJson) ? entry.ParametersJson : DBNull.Value, NpgsqlDbType = NpgsqlDbType.Jsonb });
        command.Parameters.Add(new() { Value = entry.Result });
        command.Parameters.Add(new() { Value = entry.Success });
        command.Parameters.Add(new() { Value = entry.DurationMs });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEntry>> ListAsync(int limit, string? userId = null, CancellationToken cancellationToken = default)
    {
        var sql = """
            SELECT id, at, user_id, roles, conversation_id, tool_name, kind, COALESCE(parameters::text, ''), result, success, duration_ms
            FROM audit_log
            """ + (userId is null ? "" : " WHERE user_id = $2") + " ORDER BY id DESC LIMIT $1";

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.Add(new() { Value = Math.Clamp(limit, 1, 1000) });
        if (userId is not null)
        {
            command.Parameters.Add(new() { Value = userId });
        }

        var entries = new List<AuditEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new AuditEntry(
                At: new DateTimeOffset(reader.GetFieldValue<DateTime>(1).ToUniversalTime(), TimeSpan.Zero),
                UserId: reader.GetString(2),
                Roles: reader.GetFieldValue<string[]>(3),
                ConversationId: reader.GetString(4),
                ToolName: reader.GetString(5),
                Kind: reader.GetString(6),
                ParametersJson: reader.GetString(7),
                Result: reader.GetString(8),
                Success: reader.GetBoolean(9),
                DurationMs: reader.GetInt64(10))
            {
                Id = reader.GetInt64(0),
            });
        }

        return entries;
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    private static bool IsJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(text);
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }
}
