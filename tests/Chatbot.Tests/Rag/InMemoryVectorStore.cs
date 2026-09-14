using Chatbot.Core.Rag;

namespace Chatbot.Tests.Rag;

/// <summary>
/// <see cref="IVectorStore"/> i hukommelsen med brute-force cosinus-lighed. Bruges i tests
/// i stedet for Postgres — og viser samtidig, at interfacet er databaseuafhængigt.
/// </summary>
internal sealed class InMemoryVectorStore : IVectorStore
{
    private readonly Dictionary<string, DocumentChunk> _chunks = [];

    public bool Created { get; private set; }

    public int ClearCount { get; private set; }

    public IReadOnlyCollection<DocumentChunk> Chunks => _chunks.Values;

    public Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        Created = true;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        ClearCount++;
        _chunks.Clear();
        return Task.CompletedTask;
    }

    public Task UpsertAsync(IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken = default)
    {
        foreach (var chunk in chunks)
        {
            _chunks[chunk.Id] = chunk;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SearchHit>> SearchAsync(ReadOnlyMemory<float> queryEmbedding, int top, CancellationToken cancellationToken = default)
    {
        var query = queryEmbedding.Span.ToArray();
        IReadOnlyList<SearchHit> hits = _chunks.Values
            .Select(c => new SearchHit(c with { Embedding = ReadOnlyMemory<float>.Empty }, Cosine(query, c.Embedding.Span)))
            .OrderByDescending(h => h.Score)
            .Take(top)
            .ToList();

        return Task.FromResult(hits);
    }

    public Task<long> CountAsync(CancellationToken cancellationToken = default) => Task.FromResult((long)_chunks.Count);

    private static double Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }

        return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}
