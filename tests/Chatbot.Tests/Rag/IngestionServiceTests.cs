using Chatbot.Core.Rag;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Chatbot.Tests.Rag;

public class IngestionServiceTests
{
    private static RagOptions Options(int dimensions = 8, int batchSize = 2) => new()
    {
        ChunkSize = 2000,
        ChunkOverlap = 200,
        Embedding = new EmbeddingOptions { Dimensions = dimensions, BatchSize = batchSize },
    };

    [Fact]
    public async Task RunAsync_toemmer_indekset_og_gemmer_alle_chunks_med_embeddings()
    {
        var loader = new StubLoader(
            new SourceDocument("a.md", "A", "# A\n\n## Et\n\nTekst et.\n\n## To\n\nTekst to."),
            new SourceDocument("b.md", "B", "Tekst tre."));
        var embeddings = new FakeEmbeddingGenerator();
        var store = new InMemoryVectorStore();
        var service = new IngestionService(loader, embeddings, store, Microsoft.Extensions.Options.Options.Create(Options()), NullLogger<IngestionService>.Instance);

        var result = await service.RunAsync();

        Assert.True(store.Created);
        Assert.Equal(1, store.ClearCount);
        Assert.Equal(2, result.Documents);
        Assert.Equal(3, result.Chunks);
        Assert.Equal(3, store.Chunks.Count);
        Assert.All(store.Chunks, c => Assert.Equal(8, c.Embedding.Length));
        Assert.Equal(["a.md", "b.md"], result.Sources);
    }

    [Fact]
    public async Task RunAsync_embedder_i_batches()
    {
        var loader = new StubLoader(new SourceDocument("a.md", "A", string.Join("\n\n", Enumerable.Range(1, 5).Select(i => $"## Afsnit {i}\n\nTekst {i}."))));
        var embeddings = new FakeEmbeddingGenerator();
        var service = new IngestionService(loader, embeddings, new InMemoryVectorStore(), Microsoft.Extensions.Options.Options.Create(Options(batchSize: 2)), NullLogger<IngestionService>.Instance);

        await service.RunAsync();

        // 5 chunks i batches af 2 → 3 kald.
        Assert.Equal(3, embeddings.CallCount);
        Assert.Equal(5, embeddings.Inputs.Count);
    }

    [Fact]
    public async Task RunAsync_fejler_tydeligt_hvis_dimensionen_ikke_passer()
    {
        var loader = new StubLoader(new SourceDocument("a.md", "A", "Tekst."));
        var embeddings = new FakeEmbeddingGenerator(dimensions: 8);
        var service = new IngestionService(loader, embeddings, new InMemoryVectorStore(), Microsoft.Extensions.Options.Options.Create(Options(dimensions: 768)), NullLogger<IngestionService>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync());

        Assert.Contains("768", ex.Message);
        Assert.Contains("8 dimensioner", ex.Message);
    }

    private sealed class StubLoader(params SourceDocument[] documents) : IDocumentLoader
    {
        public Task<IReadOnlyList<SourceDocument>> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SourceDocument>>(documents);
    }
}
