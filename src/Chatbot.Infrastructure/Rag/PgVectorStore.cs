using Chatbot.Core.Rag;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector;

namespace Chatbot.Infrastructure.Rag;

/// <summary>
/// <see cref="IVectorStore"/> mod Postgres med pgvector-udvidelsen.
///
/// Valget faldt på pgvector frem for Qdrant, fordi projektplanen alligevel skal have Postgres
/// til tool-registry (fase 4) og chathistorik (fase 5). Én database til det hele er én ting
/// mindre at drive. Lighed måles med cosinus (<c>&lt;=&gt;</c> er cosinus-afstand), som er
/// det, embedding-modeller som nomic-embed-text er trænet til.
/// </summary>
public sealed class PgVectorStore : IVectorStore, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _table;
    private readonly int _dimensions;
    private readonly ILogger<PgVectorStore> _logger;

    public PgVectorStore(IOptions<RagOptions> options, ILogger<PgVectorStore> logger)
    {
        var rag = options.Value;
        _table = rag.VectorStore.TableName;
        _dimensions = rag.Embedding.Dimensions;
        _logger = logger;

        var builder = new NpgsqlDataSourceBuilder(rag.VectorStore.ConnectionString);
        builder.UseVector();
        _dataSource = builder.Build();
    }

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        // Udvidelsen skal være slået til, før typen "vector" findes. Kolonnen låses til den
        // konfigurerede dimension — så fejler et forsøg på at gemme vektorer fra en anden
        // embedding-model højlydt i stedet for at blande to inkompatible vektorrum.
        var sql = $"""
            CREATE EXTENSION IF NOT EXISTS vector;

            CREATE TABLE IF NOT EXISTS {_table} (
                id          text PRIMARY KEY,
                source      text NOT NULL,
                heading     text NOT NULL,
                chunk_index integer NOT NULL,
                content     text NOT NULL,
                embedding   vector({_dimensions}) NOT NULL,
                indexed_at  timestamptz NOT NULL DEFAULT now()
            );

            CREATE INDEX IF NOT EXISTS {_table}_embedding_idx
                ON {_table} USING hnsw (embedding vector_cosine_ops);

            CREATE INDEX IF NOT EXISTS {_table}_source_idx ON {_table} (source);
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await connection.ReloadTypesAsync(cancellationToken);

        _logger.LogDebug("Tabellen {Table} (vector({Dimensions})) er klar.", _table, _dimensions);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand($"TRUNCATE TABLE {_table}");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertAsync(IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0)
        {
            return;
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var sql = $"""
            INSERT INTO {_table} (id, source, heading, chunk_index, content, embedding)
            VALUES ($1, $2, $3, $4, $5, $6)
            ON CONFLICT (id) DO UPDATE SET
                source      = EXCLUDED.source,
                heading     = EXCLUDED.heading,
                chunk_index = EXCLUDED.chunk_index,
                content     = EXCLUDED.content,
                embedding   = EXCLUDED.embedding,
                indexed_at  = now()
            """;

        foreach (var chunk in chunks)
        {
            if (chunk.Embedding.Length != _dimensions)
            {
                throw new InvalidOperationException(
                    $"Chunk '{chunk.Id}' har {chunk.Embedding.Length} dimensioner, tabellen forventer {_dimensions}.");
            }

            await using var command = new NpgsqlCommand(sql, connection, transaction)
            {
                Parameters =
                {
                    new() { Value = chunk.Id },
                    new() { Value = chunk.Source },
                    new() { Value = chunk.Heading },
                    new() { Value = chunk.ChunkIndex },
                    new() { Value = chunk.Content },
                    new() { Value = new Vector(chunk.Embedding) },
                },
            };
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        ReadOnlyMemory<float> queryEmbedding,
        int top,
        CancellationToken cancellationToken = default)
    {
        // "<=>" er cosinus-afstand (0 = identisk, 2 = modsat). Score = 1 - afstand giver
        // cosinus-lighed, som er lettere at læse: 1 = samme, ~0 = urelateret.
        var sql = $"""
            SELECT id, source, heading, chunk_index, content, 1 - (embedding <=> $1) AS score
            FROM {_table}
            ORDER BY embedding <=> $1
            LIMIT $2
            """;

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.Add(new() { Value = new Vector(queryEmbedding) });
        command.Parameters.Add(new() { Value = top });

        var hits = new List<SearchHit>(top);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var chunk = new DocumentChunk(
                Id: reader.GetString(0),
                Source: reader.GetString(1),
                Heading: reader.GetString(2),
                ChunkIndex: reader.GetInt32(3),
                Content: reader.GetString(4),
                Embedding: ReadOnlyMemory<float>.Empty);

            hits.Add(new SearchHit(chunk, reader.GetDouble(5)));
        }

        return hits;
    }

    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand($"SELECT count(*) FROM {_table}");
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long count ? count : 0;
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}
