using Chatbot.Core.Rag;
using Npgsql;

namespace Chatbot.Api.Endpoints;

/// <summary>
/// Drifts-endpoints til RAG-delen: genopbyg indekset og prøv en søgning uden at gå gennem
/// modellen. Det sidste er guld værd til fejlsøgning: svarer botten dårligt, er det første
/// spørgsmål altid "fik den de rigtige chunks?" — og det kan ses her uden at gætte.
/// </summary>
public static class RagEndpoints
{
    public static IEndpointRouteBuilder MapRagEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("").WithTags("RAG");

        group.MapPost("/ingest", async (IngestionService ingestion, CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await ingestion.RunAsync(cancellationToken);
                return Results.Ok(new IngestResponse(
                    result.Documents,
                    result.Chunks,
                    Math.Round(result.Elapsed.TotalSeconds, 1),
                    result.Sources));
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.Problem(title: "Dokumentmappen findes ikke", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(title: "Ingestion afbrudt", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (NpgsqlException ex)
            {
                return DatabaseUnavailable(ex);
            }
            catch (HttpRequestException ex)
            {
                return OllamaUnavailable(ex);
            }
        })
        .WithName("PostIngest")
        .WithSummary("Tøm og genopbyg dokumentationsindekset")
        .WithDescription(
            "Læser alle .md/.txt-filer i Rag:DocumentsPath, deler dem i chunks, genererer embeddings og " +
            "skriver dem til vektor-databasen. Kør igen efter ændringer i dokumentationen, chunking eller embedding-model.")
        .Produces<IngestResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/search", async (string q, IDocumentSearchService search, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                return Results.BadRequest(new { error = "Query-parameteren 'q' må ikke være tom." });
            }

            try
            {
                var hits = await search.SearchAsync(q, cancellationToken);
                return Results.Ok(hits.Select(h => new SearchHitDto(
                    h.Chunk.Source,
                    h.Chunk.Heading,
                    h.Chunk.ChunkIndex,
                    Math.Round(h.Score, 3),
                    h.Chunk.Content)).ToList());
            }
            catch (NpgsqlException ex)
            {
                return DatabaseUnavailable(ex);
            }
            catch (HttpRequestException ex)
            {
                return OllamaUnavailable(ex);
            }
        })
        .WithName("GetSearch")
        .WithSummary("Rå søgning i dokumentationen (uden modellen)")
        .WithDescription("Viser præcis de chunks, botten ville få at læse for spørgsmålet. Brug det til at fejlsøge retrieval.")
        .Produces<List<SearchHitDto>>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static IResult DatabaseUnavailable(Exception ex) => Results.Problem(
        title: "Vektor-databasen kunne ikke kontaktes",
        detail: $"Kører Postgres? Start den med 'docker compose up -d'. ({ex.Message})",
        statusCode: StatusCodes.Status503ServiceUnavailable);

    private static IResult OllamaUnavailable(Exception ex) => Results.Problem(
        title: "Embedding-modellen kunne ikke kontaktes",
        detail: $"Kunne ikke nå Ollama. Kører 'ollama serve', og er embedding-modellen hentet? ({ex.Message})",
        statusCode: StatusCodes.Status503ServiceUnavailable);
}

public sealed record IngestResponse(int Documents, int Chunks, double Seconds, IReadOnlyList<string> Sources);

public sealed record SearchHitDto(string Source, string Heading, int ChunkIndex, double Score, string Content);
