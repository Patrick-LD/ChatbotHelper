using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chatbot.Core.Rag;

/// <summary>Søgning i dokumentationen: spørgsmål → embedding → nærmeste chunks.</summary>
public interface IDocumentSearchService
{
    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class DocumentSearchService : IDocumentSearchService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddings;
    private readonly IVectorStore _store;
    private readonly RagOptions _options;
    private readonly ILogger<DocumentSearchService> _logger;

    public DocumentSearchService(
        IEmbeddingGenerator<string, Embedding<float>> embeddings,
        IVectorStore store,
        IOptions<RagOptions> options,
        ILogger<DocumentSearchService> logger)
    {
        _embeddings = embeddings;
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var embedding = await _embeddings.GenerateAsync(query, cancellationToken: cancellationToken);
        var hits = await _store.SearchAsync(embedding.Vector, _options.TopK, cancellationToken);

        var kept = hits.Where(h => h.Score >= _options.MinScore).ToList();

        // Hele retrieval-resultatet logges: når botten svarer mærkeligt, er det her,
        // man ser om den fik de rigtige chunks — eller slet ingen.
        _logger.LogInformation(
            "Søgning \"{Query}\": {Hits} hits, {Kept} over MinScore={MinScore}. {Summary}",
            query,
            hits.Count,
            kept.Count,
            _options.MinScore,
            string.Join(" | ", kept.Select(h => $"{h.Chunk.Source}#{h.Chunk.ChunkIndex} ({h.Score:0.000})")));

        return kept;
    }
}
