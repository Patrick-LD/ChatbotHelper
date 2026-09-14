namespace Chatbot.Core.Rag;

/// <summary>
/// Læser rå dokumenter ind fra en kilde. Fase 2 læser Markdown og tekst fra en mappe;
/// PDF, Word eller et CMS kan komme til som nye implementeringer uden at røre resten.
/// </summary>
public interface IDocumentLoader
{
    Task<IReadOnlyList<SourceDocument>> LoadAsync(CancellationToken cancellationToken = default);
}

/// <param name="Source">Identifikation af dokumentet, f.eks. filnavn relativt til dokumentmappen.</param>
/// <param name="Title">Dokumentets titel — første overskrift, ellers filnavnet.</param>
/// <param name="Text">Hele indholdet.</param>
public sealed record SourceDocument(string Source, string Title, string Text);
