using Chatbot.Core.Rag;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Chatbot.Tests.Rag;

public class DocumentSearchTests
{
    private static async Task<(DocumentSearchService Search, InMemoryVectorStore Store)> CreateIndexedAsync(
        FakeEmbeddingGenerator embeddings,
        RagOptions? options = null)
    {
        var store = new InMemoryVectorStore();
        await store.UpsertAsync(
        [
            Chunk("haandbog.md", "Ferie", 0, "Ferie\n\nDu har 25 feriedage om året og optjener ferie løbende.", embeddings),
            Chunk("haandbog.md", "Sygdom", 1, "Sygdom\n\nVed sygdom ringer du til din leder inden kl. 9.", embeddings),
            Chunk("it.md", "Adgangskoder", 0, "Adgangskoder\n\nAdgangskoden skiftes hver 90. dag i selvbetjeningsportalen.", embeddings),
        ]);

        var search = new DocumentSearchService(
            embeddings,
            store,
            Options.Create(options ?? new RagOptions { TopK = 2 }),
            NullLogger<DocumentSearchService>.Instance);

        return (search, store);
    }

    [Fact]
    public async Task SearchAsync_returnerer_de_naermeste_chunks_sorteret_efter_score()
    {
        var embeddings = new FakeEmbeddingGenerator();
        var (search, _) = await CreateIndexedAsync(embeddings);

        var hits = await search.SearchAsync("Hvor mange feriedage har jeg om året?");

        Assert.Equal(2, hits.Count);
        Assert.Equal("Ferie", hits[0].Chunk.Heading);
        Assert.True(hits[0].Score >= hits[1].Score);
    }

    [Fact]
    public async Task SearchAsync_kasserer_hits_under_MinScore()
    {
        var embeddings = new FakeEmbeddingGenerator();
        var (search, _) = await CreateIndexedAsync(embeddings, new RagOptions { TopK = 3, MinScore = 0.99 });

        var hits = await search.SearchAsync("noget helt urelateret om rumfart");

        Assert.Empty(hits);
    }

    [Fact]
    public async Task SearchAsync_med_tom_query_soeger_ikke()
    {
        var embeddings = new FakeEmbeddingGenerator();
        var (search, _) = await CreateIndexedAsync(embeddings);
        var before = embeddings.CallCount;

        var hits = await search.SearchAsync("   ");

        Assert.Empty(hits);
        Assert.Equal(before, embeddings.CallCount);
    }

    [Fact]
    public async Task Tool_formaterer_uddrag_med_kilde_og_registrerer_dem_i_RetrievalContext()
    {
        var embeddings = new FakeEmbeddingGenerator();
        var (search, _) = await CreateIndexedAsync(embeddings);
        var retrieved = new RetrievalContext();
        var tool = new DocumentSearchTool(search, retrieved);

        var text = await tool.SearchAsync("feriedage om året");

        Assert.Contains("[1] Kilde: haandbog.md — afsnit \"Ferie\"", text);
        Assert.Contains("25 feriedage", text);
        Assert.Equal(2, retrieved.Hits.Count);
    }

    [Fact]
    public async Task Tool_siger_tydeligt_naar_intet_findes()
    {
        var embeddings = new FakeEmbeddingGenerator();
        var (search, _) = await CreateIndexedAsync(embeddings, new RagOptions { TopK = 3, MinScore = 0.99 });
        var tool = new DocumentSearchTool(search, new RetrievalContext());

        var text = await tool.SearchAsync("rumfart");

        Assert.Contains("Ingen relevante uddrag", text);
    }

    [Fact]
    public async Task Tool_eksponeres_som_AIFunction_med_navn_og_beskrivelse()
    {
        var embeddings = new FakeEmbeddingGenerator();
        var (search, _) = await CreateIndexedAsync(embeddings);
        var tool = new DocumentSearchTool(search, new RetrievalContext());

        var function = Assert.IsAssignableFrom<AIFunction>(Assert.Single(tool.GetTools()));

        Assert.Equal(DocumentSearchTool.ToolName, function.Name);
        Assert.Contains("dokumentation", function.Description);
        Assert.Contains("query", function.JsonSchema.ToString());
    }

    private static DocumentChunk Chunk(string source, string heading, int index, string content, FakeEmbeddingGenerator embeddings)
        => new($"{source}#{index}", source, heading, index, content, embeddings.Embed(content));
}
