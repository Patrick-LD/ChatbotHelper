using System.Text.Json;

namespace Chatbot.Core.Chat;

/// <summary>
/// Pakker modellens svar ud, hvis den har svaret med et JSON-objekt i stedet for tekst.
///
/// llama3.1 gør det af og til, når den får flere tools: i stedet for at kalde et tool eller
/// skrive tekst svarer den <c>{"type":"message","text":"…"}</c>. Det er et formatfejl hos
/// modellen, ikke et rigtigt tool-kald, og brugeren skal ikke se rå JSON.
///
/// Fase 4-fund: får modellen IKKE et tool (rollen giver ikke adgang), kan den finde på at skrive
/// tool-kaldet som tekst: <c>{"name":"opret_medarbejder","parameters":{…}}</c>. Pipelinen udfører
/// det ikke (funktionen findes ikke), så det er ufarligt — men brugeren skal have en sætning, ikke JSON.
/// Alt andet passerer uændret.
/// </summary>
public static class ReplySanitizer
{
    public const string NoSuchToolReply =
        "Det kan jeg ikke udføre med de værktøjer, jeg har adgang til lige nu. " +
        "Kontakt den ansvarlige afdeling, eller spørg mig om noget andet.";

    private static readonly string[] TextProperties = ["text", "content", "message", "reply"];
    private static readonly string[] ArgumentProperties = ["parameters", "arguments", "args", "input"];

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
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return reply;
            }

            foreach (var name in TextProperties)
            {
                if (root.TryGetProperty(name, out var value)
                    && value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    return value.GetString()!;
                }
            }

            if (LooksLikeToolCall(root))
            {
                return NoSuchToolReply;
            }
        }
        catch (JsonException)
        {
            // Ikke gyldig JSON — så var det bare tekst, der tilfældigt starter med en tuborg.
        }

        return reply;
    }

    /// <summary>{"name": "…", "parameters": {…}} — et tool-kald skrevet som tekst i stedet for at blive kaldt.</summary>
    private static bool LooksLikeToolCall(JsonElement root)
    {
        if (!root.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()))
        {
            return false;
        }

        foreach (var property in ArgumentProperties)
        {
            if (root.TryGetProperty(property, out var args) && args.ValueKind == JsonValueKind.Object)
            {
                return true;
            }
        }

        return false;
    }
}
