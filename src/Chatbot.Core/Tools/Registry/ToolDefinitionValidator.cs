using System.Text.Json;
using System.Text.RegularExpressions;

namespace Chatbot.Core.Tools.Registry;

/// <summary>
/// Kontrollerer en tool-definition, før den gemmes. Det er den eneste værn, der er, mellem en
/// tastefejl i en database-række og en model, der får et ubrugeligt tool: navnet skal kunne
/// bruges som funktionsnavn, skemaet skal være et objekt, og opsummeringens pladsholdere
/// skal findes blandt parametrene — ellers bliver bekræftelsesteksten "Opret  ()".
/// </summary>
public static partial class ToolDefinitionValidator
{
    [GeneratedRegex("^[a-z][a-z0-9_]{0,63}$")]
    public static partial Regex ToolNamePattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,63}$")]
    public static partial Regex RoleNamePattern();

    public static IReadOnlyList<string> Validate(ToolDefinition tool, IEnumerable<IToolHandler> handlers)
    {
        var errors = new List<string>();

        if (!ToolNamePattern().IsMatch(tool.Name))
        {
            errors.Add("Navnet skal være 1-64 tegn: små bogstaver, tal og underscore, og starte med et bogstav (f.eks. opret_medarbejder).");
        }

        if (string.IsNullOrWhiteSpace(tool.Description))
        {
            errors.Add("Beskrivelsen mangler — det er den tekst, modellen vælger toolet ud fra.");
        }

        var properties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (tool.ParametersSchema.ValueKind != JsonValueKind.Object)
        {
            errors.Add("parametersSchema skal være et JSON-skema-objekt.");
        }
        else
        {
            if (!tool.ParametersSchema.TryGetProperty("type", out var type) || type.GetString() != "object")
            {
                errors.Add("parametersSchema skal have \"type\": \"object\".");
            }

            if (tool.ParametersSchema.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in props.EnumerateObject())
                {
                    properties.Add(p.Name);
                }
            }

            if (tool.ParametersSchema.TryGetProperty("required", out var required))
            {
                if (required.ValueKind != JsonValueKind.Array)
                {
                    errors.Add("parametersSchema.required skal være en liste af feltnavne.");
                }
                else
                {
                    foreach (var r in required.EnumerateArray())
                    {
                        var name = r.GetString();
                        if (name is not null && !properties.Contains(name))
                        {
                            errors.Add($"parametersSchema.required nævner '{name}', som ikke findes i properties.");
                        }
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(tool.SummaryTemplate))
        {
            foreach (var placeholder in TemplateRenderer.Placeholders(tool.SummaryTemplate))
            {
                if (!properties.Contains(placeholder))
                {
                    errors.Add($"summaryTemplate bruger {{{placeholder}}}, som ikke er en parameter i skemaet.");
                }
            }
        }
        else if (tool.RequiresConfirmation)
        {
            errors.Add("Tools der kræver bekræftelse skal have en summaryTemplate — det er den tekst, brugeren siger ja til.");
        }

        foreach (var role in tool.Roles)
        {
            if (!RoleNamePattern().IsMatch(role))
            {
                errors.Add($"Rollenavnet '{role}' er ugyldigt (små bogstaver, tal, bindestreg og underscore).");
            }
        }

        var handler = handlers.FirstOrDefault(h => h.HandlerType == tool.HandlerType);
        if (handler is null)
        {
            errors.Add($"Der er ingen handler for tool-typen {tool.HandlerType}.");
        }
        else
        {
            errors.AddRange(handler.Validate(tool.HandlerConfig));
        }

        return errors;
    }
}
