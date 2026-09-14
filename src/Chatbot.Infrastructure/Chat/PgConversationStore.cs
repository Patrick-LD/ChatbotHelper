using Chatbot.Core.Chat;
using Chatbot.Core.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Chatbot.Infrastructure.Chat;

/// <summary>
/// Chathistorik i Postgres (fase 5.2), så samtaler overlever genstart. To tabeller:
/// <c>conversations</c> (id, ejer, tidspunkter) og <c>messages</c> (rolle, indhold, rækkefølge).
/// Kun bruger- og assistentbeskeder gemmes — tool-kald og tool-svar er turens interne arbejde.
/// </summary>
public sealed class PgConversationStore : IConversationStore, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public PgConversationStore(IOptions<ToolsOptions> options)
        => _dataSource = NpgsqlDataSource.Create(options.Value.Registry.ConnectionString);

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS conversations (
                id         text PRIMARY KEY,
                user_id    text NOT NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                updated_at timestamptz NOT NULL DEFAULT now()
            );

            CREATE INDEX IF NOT EXISTS conversations_user_idx ON conversations (user_id, updated_at DESC);

            CREATE TABLE IF NOT EXISTS messages (
                id              bigserial PRIMARY KEY,
                conversation_id text NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,
                role            text NOT NULL CHECK (role IN ('user', 'assistant', 'system')),
                content         text NOT NULL,
                created_at      timestamptz NOT NULL DEFAULT now()
            );

            CREATE INDEX IF NOT EXISTS messages_conversation_idx ON messages (conversation_id, id);
            """;

        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<string?> GetOwnerAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand("SELECT user_id FROM conversations WHERE id = $1");
        command.Parameters.Add(new() { Value = conversationId });
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async Task<IReadOnlyList<ChatMessage>> GetAsync(string conversationId, int maxMessages = 0, CancellationToken cancellationToken = default)
    {
        // De seneste N hentes nyeste-først med LIMIT og vendes bagefter — så koster en lang samtale ikke mere end en kort.
        var sql = maxMessages > 0
            ? "SELECT role, content FROM (SELECT id, role, content FROM messages WHERE conversation_id = $1 ORDER BY id DESC LIMIT $2) t ORDER BY id"
            : "SELECT role, content FROM messages WHERE conversation_id = $1 ORDER BY id";

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.Add(new() { Value = conversationId });
        if (maxMessages > 0)
        {
            command.Parameters.Add(new() { Value = maxMessages });
        }

        var messages = new List<ChatMessage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            messages.Add(new ChatMessage(new ChatRole(reader.GetString(0)), reader.GetString(1)));
        }

        return messages;
    }

    public async Task AppendAsync(string conversationId, string userId, IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var upsert = new NpgsqlCommand(
                         "INSERT INTO conversations (id, user_id) VALUES ($1, $2) ON CONFLICT (id) DO UPDATE SET updated_at = now()",
                         connection, transaction))
        {
            upsert.Parameters.Add(new() { Value = conversationId });
            upsert.Parameters.Add(new() { Value = userId });
            await upsert.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var message in messages)
        {
            await using var insert = new NpgsqlCommand(
                "INSERT INTO messages (conversation_id, role, content) VALUES ($1, $2, $3)", connection, transaction);
            insert.Parameters.Add(new() { Value = conversationId });
            insert.Parameters.Add(new() { Value = message.Role.Value });
            insert.Parameters.Add(new() { Value = message.Text });
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}
