using System.Text.Json;
using System.Text.RegularExpressions;

namespace Chatbot.Core.Tools.Registry;

/// <summary>
/// Erstatter <c>{navn}</c>-pladsholdere med værdier. Bruges til opsummeringer ("Opret {fuldeNavn} …"),
/// URL'er, body-skabeloner og svar-skabeloner i HTTP-tools. Bevidst simpelt: ingen udtryk, ingen
/// betingelser — det skal kunne skrives af en kollega uden at læse kode.
/// </summary>
public static partial class TemplateRenderer
{
    [GeneratedRegex(@"\{([A-Za-z0-9_.\-]+)\}")]
    private static partial Regex Placeholder();

    public static bool HasPlaceholders(string template) => Placeholder().IsMatch(template);

    /// <summary>Navnene på pladsholderne i skabelonen, i rækkefølge og uden dubletter.</summary>
    public static IReadOnlyList<string> Placeholders(string template) =>
        Placeholder().Matches(template).Select(m => m.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Erstatter pladsholdere; ukendte pladsholdere bliver tomme. Værdier kan efterbehandles (f.eks. URL-kodes).</summary>
    public static string Render(string template, IReadOnlyDictionary<string, string> values, Func<string, string>? transform = null)
        => Placeholder().Replace(template, m =>
        {
            var value = values.TryGetValue(m.Groups[1].Value, out var v) ? v : string.Empty;
            return transform is null ? value : transform(value);
        });

    /// <summary>Fladt opslag fra et JSON-objekt: hver top-egenskab som tekst (tal og bool uden anførselstegn, null som tom).</summary>
    public static IReadOnlyDictionary<string, string> Flatten(JsonElement json)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (json.ValueKind != JsonValueKind.Object)
        {
            return values;
        }

        foreach (var property in json.EnumerateObject())
        {
            values[property.Name] = AsText(property.Value);
        }

        return values;
    }

    public static string AsText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        _ => value.GetRawText(),
    };
}
