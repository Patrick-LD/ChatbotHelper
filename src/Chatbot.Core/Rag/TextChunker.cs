using System.Text;

namespace Chatbot.Core.Rag;

/// <summary>
/// Deler et dokument op i chunks, der kan embeddes hver for sig.
///
/// Strategien er bevidst simpel (projektplanen: "start simpelt, finjustér ud fra evalueringssættet"):
/// 1. Teksten deles ved Markdown-overskrifter, så en chunk aldrig blander to emner, og så hver
///    chunk kender sin overskrift — det er den metadata, botten bruger til at henvise til kilden.
/// 2. Er et afsnit længere end <c>chunkSize</c>, deles det videre i stykker med overlap, og der
///    klippes helst ved et afsnitsskift eller en sætning, ikke midt i et ord.
/// </summary>
public sealed class TextChunker
{
    private readonly int _chunkSize;
    private readonly int _overlap;

    public TextChunker(int chunkSize, int overlap)
    {
        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk-størrelsen skal være positiv.");
        }

        if (overlap < 0 || overlap >= chunkSize)
        {
            throw new ArgumentOutOfRangeException(nameof(overlap), "Overlappet skal være mindre end chunk-størrelsen.");
        }

        _chunkSize = chunkSize;
        _overlap = overlap;
    }

    public IReadOnlyList<DocumentChunk> Chunk(SourceDocument document)
    {
        var chunks = new List<DocumentChunk>();
        var index = 0;

        foreach (var section in SplitByHeadings(document))
        {
            foreach (var piece in SplitBySize(section.Body))
            {
                // Overskriften gentages forrest i teksten, så embeddingen også "ved", hvad
                // afsnittet handler om — det gør en forskel for korte chunks.
                var content = $"{section.Heading}\n\n{piece}";
                chunks.Add(new DocumentChunk(
                    Id: $"{document.Source}#{index}",
                    Source: document.Source,
                    Heading: section.Heading,
                    ChunkIndex: index,
                    Content: content,
                    Embedding: ReadOnlyMemory<float>.Empty));
                index++;
            }
        }

        return chunks;
    }

    private static IEnumerable<(string Heading, string Body)> SplitByHeadings(SourceDocument document)
    {
        var heading = document.Title;
        var body = new StringBuilder();

        foreach (var rawLine in document.Text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (IsHeading(line, out var text))
            {
                if (body.ToString().Trim().Length > 0)
                {
                    yield return (heading, body.ToString().Trim());
                }

                heading = text;
                body.Clear();
                continue;
            }

            body.AppendLine(line);
        }

        if (body.ToString().Trim().Length > 0)
        {
            yield return (heading, body.ToString().Trim());
        }
    }

    private static bool IsHeading(string line, out string text)
    {
        text = string.Empty;
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith('#'))
        {
            return false;
        }

        var level = trimmed.TakeWhile(c => c == '#').Count();
        if (level > 6 || trimmed.Length <= level || trimmed[level] != ' ')
        {
            return false;
        }

        text = trimmed[(level + 1)..].Trim();
        return text.Length > 0;
    }

    private IEnumerable<string> SplitBySize(string text)
    {
        if (text.Length <= _chunkSize)
        {
            yield return text;
            yield break;
        }

        var start = 0;
        while (start < text.Length)
        {
            var end = Math.Min(start + _chunkSize, text.Length);

            if (end < text.Length)
            {
                end = FindBreak(text, start, end);
            }

            var piece = text[start..end].Trim();
            if (piece.Length > 0)
            {
                yield return piece;
            }

            if (end >= text.Length)
            {
                yield break;
            }

            // Næste chunk starter lidt før denne sluttede, så kontekst på grænsen bevares.
            start = Math.Max(end - _overlap, start + 1);
        }
    }

    /// <summary>
    /// Finder et pænt klippested i den sidste tredjedel af vinduet: helst et afsnitsskift,
    /// ellers et sætningsskift, ellers et mellemrum. Findes intet, klippes hårdt.
    /// </summary>
    private int FindBreak(string text, int start, int end)
    {
        var floor = start + (_chunkSize * 2 / 3);
        var window = end - start;

        var paragraph = text.LastIndexOf("\n\n", end - 1, window - 1, StringComparison.Ordinal);
        if (paragraph >= floor)
        {
            return paragraph;
        }

        var sentence = text.LastIndexOfAny(['.', '!', '?', '\n'], end - 1, window);
        if (sentence >= floor)
        {
            return sentence + 1;
        }

        var space = text.LastIndexOf(' ', end - 1, window);
        return space >= floor ? space : end;
    }
}
