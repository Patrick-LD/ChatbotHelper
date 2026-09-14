using System.Text.Json;
using Chatbot.Core.Rag;

namespace Chatbot.Core.Tools.Registry;

/// <summary>
/// Fase 3's hardcodede tools som rækker (4.1: "Migrér de hardcodede tools ind i tabellen").
/// Indsættes kun i et tomt registry — derefter er databasen sandheden, og ændringer laves dér
/// (eller via /tools), ikke her. Beskrivelserne er ordret dem, der bestod fase 3-evalueringen.
/// </summary>
public static class ToolSeed
{
    public const string RoleEmployee = "medarbejder";
    public const string RoleHr = "hr";

    public static IReadOnlyList<RoleDefinition> Roles =>
    [
        new(RoleEmployee, "Alle medarbejdere: må søge i dokumentationen og slå kolleger op."),
        new(RoleHr, "HR-administratorer: må desuden oprette medarbejdere i PersonaleNet."),
    ];

    public static IReadOnlyList<ToolDefinition> Default(string employeeApiBaseUrl)
    {
        var baseUrl = employeeApiBaseUrl.TrimEnd('/');

        return
        [
            new ToolDefinition(
                Name: DocumentSearchTool.ToolName,
                Description:
                    "Søger i virksomhedens interne dokumentation (personalehåndbog, vejledninger, processer, IT-systemer) " +
                    "og returnerer de mest relevante uddrag med kildehenvisning. Brug dette tool, hver gang brugeren spørger " +
                    "om virksomhedens regler, procedurer, systemer eller hvordan man gør noget internt — også når du tror, du " +
                    "kender svaret. Brug det ikke til småsnak eller almen viden, der ikke handler om virksomheden.",
                ParametersSchema: Json("""
                    {
                      "type": "object",
                      "properties": {
                        "query": { "type": "string", "description": "Brugerens spørgsmål eller emne, formuleret så præcist som muligt, på dansk." }
                      },
                      "required": ["query"]
                    }
                    """),
                HandlerType: ToolHandlerType.Internal,
                HandlerConfig: Json($$"""{ "handler": "{{DocumentSearchTool.HandlerKey}}" }"""),
                RequiresConfirmation: false,
                SummaryTemplate: null,
                IsActive: true,
                Roles: [RoleEmployee, RoleHr]),

            new ToolDefinition(
                Name: "find_medarbejder",
                Description:
                    "Slår medarbejdere op i HR-systemet PersonaleNet efter navn (eller alle, hvis navnet udelades). " +
                    "Brug det, når brugeren spørger, om en person er oprettet, eller vil se en medarbejders oplysninger. " +
                    "Kræver ingen bekræftelse — det ændrer ikke noget.",
                ParametersSchema: Json("""
                    {
                      "type": "object",
                      "properties": {
                        "navn": { "type": "string", "description": "Hele eller dele af navnet. Tom streng giver alle medarbejdere." }
                      },
                      "required": []
                    }
                    """),
                HandlerType: ToolHandlerType.Http,
                HandlerConfig: Json($$"""
                    {
                      "method": "GET",
                      "url": "{{baseUrl}}/employees?name={navn}",
                      "timeoutSeconds": 15,
                      "successMessage": "Fandt {count} medarbejder(e):",
                      "itemTemplate": "- #{id} {fullName}, {jobTitle} i {department}, {email}, start {startDate}",
                      "emptyMessage": "Ingen medarbejder matcher \"{navn}\" i HR-systemet (eller der er slet ingen medarbejdere).",
                      "errorMessage": "HR-systemet afviste opslaget: {error}"
                    }
                    """),
                RequiresConfirmation: false,
                SummaryTemplate: null,
                IsActive: true,
                Roles: [RoleEmployee, RoleHr]),

            new ToolDefinition(
                Name: "opret_medarbejder",
                Description:
                    "Forbereder oprettelse af en ny medarbejder i HR-systemet PersonaleNet. Brug dette tool, når brugeren " +
                    "beder dig om at oprette, tilføje eller ansætte en medarbejder. Alle fem oplysninger er påkrævede — mangler " +
                    "en af dem, må du IKKE kalde toolet med gæt eller pladsholdere; spørg brugeren i stedet. Toolet opretter " +
                    "ikke selv medarbejderen: det returnerer en opsummering, som du skal vise brugeren og bede om at bekræfte. " +
                    "Brug det ikke til at forklare, hvordan man opretter en medarbejder — det er dokumentationssøgningens job.",
                ParametersSchema: Json("""
                    {
                      "type": "object",
                      "properties": {
                        "fuldeNavn": { "type": "string", "description": "Medarbejderens fulde navn, f.eks. \"Lars Hansen\"." },
                        "email":     { "type": "string", "format": "email", "description": "Medarbejderens e-mailadresse." },
                        "afdeling":  { "type": "string", "description": "Afdelingen medarbejderen skal ansættes i, f.eks. \"Salg\"." },
                        "stilling":  { "type": "string", "description": "Stillingsbetegnelse, f.eks. \"Konsulent\"." },
                        "startdato": { "type": "string", "format": "date", "description": "Første arbejdsdag som yyyy-MM-dd, f.eks. \"2026-10-01\"." }
                      },
                      "required": ["fuldeNavn", "email", "afdeling", "stilling", "startdato"]
                    }
                    """),
                HandlerType: ToolHandlerType.Http,
                HandlerConfig: Json($$"""
                    {
                      "method": "POST",
                      "url": "{{baseUrl}}/employees",
                      "timeoutSeconds": 15,
                      "body": {
                        "fullName":   "{fuldeNavn}",
                        "email":      "{email}",
                        "department": "{afdeling}",
                        "jobTitle":   "{stilling}",
                        "startDate":  "{startdato}"
                      },
                      "successMessage": "{fullName} er nu oprettet i PersonaleNet som {jobTitle} i {department} med start {startDate} (medarbejder-id {id}). Brugerkonto og udstyr bestilles automatisk natten før startdatoen.",
                      "errorMessage": "HR-systemet afviste oprettelsen: {error} Ret oplysningerne, og prøv igen."
                    }
                    """),
                RequiresConfirmation: true,
                SummaryTemplate: "Opret {fuldeNavn} ({email}) som {stilling} i {afdeling} med start {startdato}",
                IsActive: true,
                Roles: [RoleHr]),
        ];
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
