using Chatbot.Core.Rag;

namespace Chatbot.Tests.Rag;

public class TextChunkerTests
{
    [Fact]
    public void Chunk_deler_ved_overskrifter_og_gemmer_overskriften_som_metadata()
    {
        var chunker = new TextChunker(chunkSize: 2000, overlap: 200);
        var document = new SourceDocument("haandbog.md", "Personalehåndbog", """
            # Personalehåndbog

            Velkommen til virksomheden.

            ## Ferie

            Du har 25 feriedage om året.

            ## Sygdom

            Ring til din leder inden kl. 9.
            """);

        var chunks = chunker.Chunk(document);

        Assert.Equal(3, chunks.Count);
        Assert.Equal(["Personalehåndbog", "Ferie", "Sygdom"], chunks.Select(c => c.Heading).ToArray());
        Assert.All(chunks, c => Assert.Equal("haandbog.md", c.Source));
        Assert.Equal([0, 1, 2], chunks.Select(c => c.ChunkIndex).ToArray());
        Assert.Equal(["haandbog.md#0", "haandbog.md#1", "haandbog.md#2"], chunks.Select(c => c.Id).ToArray());
    }

    [Fact]
    public void Chunk_saetter_overskriften_forrest_i_teksten_der_embeddes()
    {
        var chunker = new TextChunker(2000, 200);
        var document = new SourceDocument("a.md", "Titel", "## Ferie\n\nDu har 25 feriedage.");

        var chunk = Assert.Single(chunker.Chunk(document));

        Assert.StartsWith("Ferie\n\n", chunk.Content);
        Assert.Contains("25 feriedage", chunk.Content);
    }

    [Fact]
    public void Chunk_deler_lange_afsnit_med_overlap()
    {
        var chunker = new TextChunker(chunkSize: 300, overlap: 50);
        var sentences = Enumerable.Range(1, 40).Select(i => $"Dette er sætning nummer {i} i et meget langt afsnit.");
        var document = new SourceDocument("lang.md", "Lang", string.Join(" ", sentences));

        var chunks = chunker.Chunk(document);

        Assert.True(chunks.Count > 3, $"Forventede flere chunks, fik {chunks.Count}.");
        Assert.All(chunks, c => Assert.True(c.Content.Length <= 300 + "Lang\n\n".Length + 1));

        // Overlap: slutningen af én chunk går igen i starten af den næste.
        for (var i = 1; i < chunks.Count; i++)
        {
            var previous = chunks[i - 1].Content;
            var tail = previous[^30..];
            Assert.Contains(tail.Split(' ')[^1], chunks[i].Content);
        }
    }

    [Fact]
    public void Chunk_klipper_ved_saetningsgraense_og_ikke_midt_i_ord()
    {
        var chunker = new TextChunker(chunkSize: 120, overlap: 10);
        var text = string.Join(" ", Enumerable.Range(1, 12).Select(i => $"Sætning {i} slutter her."));
        var document = new SourceDocument("s.md", "S", text);

        var chunks = chunker.Chunk(document);

        Assert.All(chunks.SkipLast(1), c => Assert.EndsWith(".", c.Content));
    }

    [Fact]
    public void Chunk_bruger_dokumenttitel_som_overskrift_for_tekst_uden_overskrifter()
    {
        var chunker = new TextChunker(2000, 200);
        var document = new SourceDocument("noter.txt", "noter", "Bare noget tekst uden overskrifter.");

        var chunk = Assert.Single(chunker.Chunk(document));

        Assert.Equal("noter", chunk.Heading);
    }

    [Fact]
    public void Chunk_springer_tomme_afsnit_over()
    {
        var chunker = new TextChunker(2000, 200);
        var document = new SourceDocument("a.md", "A", "# A\n\n## Tomt afsnit\n\n## Med indhold\n\nHer er noget.");

        var chunks = chunker.Chunk(document);

        Assert.Single(chunks);
        Assert.Equal("Med indhold", chunks[0].Heading);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 100)]
    [InlineData(100, 150)]
    public void Constructor_afviser_ugyldige_parametre(int size, int overlap)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextChunker(size, overlap));
    }
}
