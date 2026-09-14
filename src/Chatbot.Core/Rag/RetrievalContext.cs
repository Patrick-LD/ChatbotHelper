namespace Chatbot.Core.Rag;

/// <summary>
/// Samler de chunks, der blev hentet i løbet af én tur (scoped pr. request), så svaret
/// kan sendes tilbage med kildehenvisninger — og så evalueringen kan se, hvad botten fik at læse.
/// </summary>
public sealed class RetrievalContext
{
    private readonly List<SearchHit> _hits = [];

    public IReadOnlyList<SearchHit> Hits => _hits;

    public void Add(IEnumerable<SearchHit> hits) => _hits.AddRange(hits);
}
