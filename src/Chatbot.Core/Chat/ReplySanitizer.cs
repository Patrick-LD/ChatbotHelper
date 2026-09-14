using System.Text.Json;

namespace Chatbot.Core.Chat;

/// <summary>
/// Pakker modellens svar ud, hvis den har svaret med et JSON-objekt i stedet for tekst.
///
/// llama3.1 gør det af og til, når den får flere tools: i stedet for at kalde et tool eller
/// skrive tekst svarer den <c>{"type":"message","text":"…"}</c>. Det er et formatfejl hos
/// modellen, ikke et rigtigt tool-kald, og brugeren skal ikke se rå JSON. Alt andet passerer uændret.
/// </summary>
public static class ReplySanitizer
{
    private static readonly string[] TextProperties = ["text", "content", "message", "reply"];

    public static string Unwrap(string reply)
    {
        var trimmed = reply.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '{' || trimmed[^1] != '}')
        {
            return reply;
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return reply;
            }

            foreach (var name in TextProperties)
            {
                if (doc.RootElement.TryGetProperty(name, out var value)
                    && value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    return value.GetString()!;
                }
            }
        }
        catch (JsonException)
        {
            // Ikke gyldig JSON — så var det bare tekst, der tilfældigt starter med en tuborg.
        }

        return reply;
    }
}
