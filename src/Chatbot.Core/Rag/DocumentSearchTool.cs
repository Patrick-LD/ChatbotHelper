using System.ComponentModel;
using System.Text;
using Chatbot.Core.Chat;
using Microsoft.Extensions.AI;

namespace Chatbot.Core.Rag;

/// <summary>
/// Eksponerer dokumentationssøgningen som et tool, modellen selv kan vælge at kalde.
/// Det er projektplanens bærende princip: "dokumentationssøgning er bare endnu et tool".
/// I fase 3 kommer handlings-tools til på samme måde, og i fase 4 loades de fra databasen.
///
/// Beskrivelsen på metoden er ikke pynt — det er den tekst, modellen læser, når den skal
/// beslutte, om den skal søge. Skriv den som til en ny kollega: hvornår, og hvornår ikke.
/// </summary>
public sealed class DocumentSearchTool : IChatToolProvider
{
    public const string ToolName = "soeg_i_dokumentation";

    private readonly IDocumentSearchService _search;
    private readonly RetrievalContext _retrieved;

    public DocumentSearchTool(IDocumentSearchService search, RetrievalContext retrieved)
    {
        _search = search;
        _retrieved = retrieved;
    }

    public IReadOnlyList<AITool> GetTools() =>
    [
        AIFunctionFactory.Create(SearchAsync, ToolName),
    ];

    [Description(
        "Søger i virksomhedens interne dokumentation (personalehåndbog, vejledninger, processer, IT-systemer) " +
        "og returnerer de mest relevante uddrag med kildehenvisning. Brug dette tool, hver gang brugeren spørger " +
        "om virksomhedens regler, procedurer, systemer eller hvordan man gør noget internt — også når du tror, du " +
        "kender svaret. Brug det ikke til småsnak eller almen viden, der ikke handler om virksomheden.")]
    public async Task<string> SearchAsync(
        [Description("Brugerens spørgsmål eller emne, formuleret så præcist som muligt, på dansk.")] string query,
        CancellationToken cancellationToken = default)
    {
        var hits = await _search.SearchAsync(query, cancellationToken);
        _retrieved.Add(hits);

        if (hits.Count == 0)
        {
            return "Ingen relevante uddrag fundet i dokumentationen. Sig ærligt til brugeren, at dokumentationen ikke dækker spørgsmålet.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Fandt {hits.Count} uddrag. Svar ud fra dem og henvis til kilden [nummer] i dit svar:");
        for (var i = 0; i < hits.Count; i++)
        {
            var hit = hits[i];
            sb.AppendLine();
            sb.AppendLine($"[{i + 1}] Kilde: {hit.Chunk.Source} — afsnit \"{hit.Chunk.Heading}\"");
            sb.AppendLine(hit.Chunk.Content);
        }

        return sb.ToString();
    }
}
