using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chatbot.Core.Rag;

/// <summary>
/// Ingestion-pipelinen: dokumenter → chunks → embeddings → vektor-database.
/// Kører som ét genkørbart job, der tømmer og genopbygger hele indekset. Det er bevidst:
/// skiftes embedding-model eller chunking, er det eneste rigtige at starte forfra.
/// </summary>
public sealed class IngestionService
{
    private readonly IDocumentLoader _loader;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddings;
    private readonly IVectorStore _store;
    private readonly RagOptions _options;
    private readonly ILogger<IngestionService> _logger;

    public IngestionService(
        IDocumentLoader loader,
        IEmbeddingGenerator<string, Embedding<float>> embeddings,
        IVectorStore store,
        IOptions<RagOptions> options,
        ILogger<IngestionService> logger)
    {
        _loader = loader;
        _embeddings = embeddings;
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IngestionResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var documents = await _loader.LoadAsync(cancellationToken);
        var chunker = new TextChunker(_options.ChunkSize, _options.ChunkOverlap);
        var chunks = documents.SelectMany(chunker.Chunk).ToList();

        _logger.LogInformation(
            "Ingestion: {Documents} dokumenter → {Chunks} chunks (chunkSize={ChunkSize}, overlap={Overlap}).",
            documents.Count, chunks.Count, _options.ChunkSize, _options.ChunkOverlap);

        await _store.EnsureCreatedAsync(cancellationToken);
        await _store.ClearAsync(cancellationToken);

        var written = 0;
        foreach (var batch in chunks.Chunk(_options.Embedding.BatchSize))
        {
            var embeddings = await _embeddings.GenerateAsync(
                batch.Select(c => c.Content).ToList(),
                cancellationToken: cancellationToken);

            var withVectors = new List<DocumentChunk>(batch.Length);
            for (var i = 0; i < batch.Length; i++)
            {
                var vector = embeddings[i].Vector;
                if (vector.Length != _options.Embedding.Dimensions)
                {
                    throw new InvalidOperationException(
                        $"Embedding-modellen '{_options.Embedding.Model}' gav {vector.Length} dimensioner, men " +
                        $"Rag:Embedding:Dimensions er {_options.Embedding.Dimensions}. Ret konfigurationen og kør ingestion igen.");
                }

                withVectors.Add(batch[i] with { Embedding = vector });
            }

            await _store.UpsertAsync(withVectors, cancellationToken);
            written += withVectors.Count;
            _logger.LogDebug("Ingestion: {Written}/{Total} chunks gemt.", written, chunks.Count);
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "Ingestion færdig: {Chunks} chunks fra {Documents} dokumenter på {Elapsed:N1} s.",
            written, documents.Count, stopwatch.Elapsed.TotalSeconds);

        return new IngestionResult(
            documents.Count,
            written,
            stopwatch.Elapsed,
            documents.Select(d => d.Source).ToList());
    }
}

/// <param name="Documents">Antal indlæste dokumenter.</param>
/// <param name="Chunks">Antal chunks skrevet til indekset.</param>
/// <param name="Elapsed">Samlet tid inkl. embeddings.</param>
/// <param name="Sources">Kilderne der blev indekseret — praktisk til at se, at den rigtige mappe blev læst.</param>
public sealed record IngestionResult(int Documents, int Chunks, TimeSpan Elapsed, IReadOnlyList<string> Sources);
