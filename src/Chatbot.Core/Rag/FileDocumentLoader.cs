using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chatbot.Core.Rag;

/// <summary>
/// Læser alle .md- og .txt-filer i dokumentmappen (rekursivt). Titel er den første
/// Markdown-overskrift; findes der ingen, bruges filnavnet.
/// </summary>
public sealed class FileDocumentLoader : IDocumentLoader
{
    private static readonly string[] Extensions = [".md", ".txt"];

    private readonly RagOptions _options;
    private readonly ILogger<FileDocumentLoader> _logger;

    public FileDocumentLoader(IOptions<RagOptions> options, ILogger<FileDocumentLoader> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SourceDocument>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(_options.DocumentsPath);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(
                $"Dokumentmappen '{root}' findes ikke. Sæt Rag:DocumentsPath, eller opret mappen og læg .md/.txt-filer i den.");
        }

        var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        var documents = new List<SourceDocument>(files.Count);
        foreach (var file in files)
        {
            var text = await File.ReadAllTextAsync(file, cancellationToken);
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning("Springer tom fil over: {File}", file);
                continue;
            }

            var source = Path.GetRelativePath(root, file).Replace('\\', '/');
            documents.Add(new SourceDocument(source, ExtractTitle(text, source), text));
        }

        _logger.LogInformation("Indlæste {Count} dokumenter fra {Root}.", documents.Count, root);
        return documents;
    }

    private static string ExtractTitle(string text, string fallback)
    {
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                return line[2..].Trim();
            }
        }

        return Path.GetFileNameWithoutExtension(fallback);
    }
}
