using Chatbot.Core.Actions;
using Chatbot.Core.Tools;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Chatbot.Infrastructure.Chat;

/// <summary>
/// Ventende handlinger i Postgres (fase 5.2). Højst én pr. samtale — primærnøglen håndhæver det.
/// Følger chathistorikken: et forslag må ikke forsvinde ved en genstart, og det må heller ikke
/// overleve i ét API-instans' hukommelse, mens et andet instans svarer brugeren.
/// </summary>
public sealed class PgPendingActionStore : IPendingActionStore, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public PgPendingActionStore(IOptions<ToolsOptions> options)
        => _dataSource = NpgsqlDataSource.Create(options.Value.Registry.ConnectionString);

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS pending_actions (
                conversation_id text PRIMARY KEY,
                tool_name       text NOT NULL,
                summary         text NOT NULL,
                parameters      jsonb NOT NULL,
                created_at      timestamptz NOT NULL
            );
            """;

        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PendingAction?> GetAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand(
            "SELECT tool_name, summary, parameters::text, created_at FROM pending_actions WHERE conversation_id = $1");
        command.Parameters.Add(new() { Value = conversationId });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new PendingAction(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            new DateTimeOffset(reader.GetFieldValue<DateTime>(3).ToUniversalTime(), TimeSpan.Zero));
    }

    public async Task SetAsync(string conversationId, PendingAction action, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO pending_actions (conversation_id, tool_name, summary, parameters, created_at)
            VALUES ($1, $2, $3, $4, $5)
            ON CONFLICT (conversation_id) DO UPDATE SET
                tool_name  = EXCLUDED.tool_name,
                summary    = EXCLUDED.summary,
                parameters = EXCLUDED.parameters,
                created_at = EXCLUDED.created_at
            """;

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.Add(new() { Value = conversationId });
        command.Parameters.Add(new() { Value = action.ToolName });
        command.Parameters.Add(new() { Value = action.Summary });
        command.Parameters.Add(new() { Value = action.ParametersJson, NpgsqlDbType = NpgsqlDbType.Jsonb });
        command.Parameters.Add(new() { Value = action.CreatedAt.UtcDateTime });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ClearAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand("DELETE FROM pending_actions WHERE conversation_id = $1");
        command.Parameters.Add(new() { Value = conversationId });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}
