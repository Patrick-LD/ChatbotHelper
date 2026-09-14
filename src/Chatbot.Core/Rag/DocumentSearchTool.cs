using System.Text;
using System.Text.Json;
using Chatbot.Core.Tools.Registry;

namespace Chatbot.Core.Rag;

/// <summary>
/// Dokumentationssøgningen som et tool, modellen selv kan vælge at kalde.
/// Det er projektplanens bærende princip: "dokumentationssøgning er bare endnu et tool".
///
/// Fra fase 4 er klassen en <see cref="IInternalTool"/>: navn, beskrivelse, parametre og roller
/// står i tool-registret (rækken <c>soeg_i_dokumentation</c> med handler-typen <c>internal</c> og
/// nøglen <see cref="HandlerKey"/>) — koden her leverer kun udførelsen. Beskrivelsen, modellen læser,
/// er derfor ikke længere en attribut i koden, men en kolonne i databasen (se <see cref="ToolSeed"/>).
/// </summary>
public sealed class DocumentSearchTool : IInternalTool
{
    /// <summary>Tool-navnet i registret. Bruges af seed og evaluering.</summary>
    public const string ToolName = "soeg_i_dokumentation";

    /// <summary>Nøglen registret peger på i handlerConfig: { "handler": "dokumentationssoegning" }.</summary>
    public const string HandlerKey = "dokumentationssoegning";

    private readonly IDocumentSearchService _search;
    private readonly RetrievalContext _retrieved;

    public DocumentSearchTool(IDocumentSearchService search, RetrievalContext retrieved)
    {
        _search = search;
        _retrieved = retrieved;
    }

    public string Key => HandlerKey;

    public Task<string> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var query = arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("query", out var q)
            ? TemplateRenderer.AsText(q)
            : string.Empty;

        return SearchAsync(query, cancellationToken);
    }

    public async Task<string> SearchAsync(string query, CancellationToken cancellationToken = default)
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
