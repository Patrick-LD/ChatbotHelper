using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Chatbot.Core.Actions;
using Chatbot.Core.Chat;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Chatbot.Core.Tools;

/// <summary>
/// Fase 3's handlings-tools: opret og find medarbejdere i HR-systemet.
///
/// <c>opret_medarbejder</c> udfører IKKE handlingen. Den validerer oplysningerne, gemmer en
/// <see cref="PendingAction"/> på samtalen og beder modellen om at få brugerens bekræftelse.
/// Selve oprettelsen sker i <see cref="ExecuteAsync"/>, som orkestratoren kalder, når brugeren
/// har sagt ja. Modellen kan altså ikke oprette nogen — den kan kun foreslå.
/// </summary>
public sealed class EmployeeTools : IChatToolProvider, IActionExecutor
{
    public const string CreateToolName = "opret_medarbejder";
    public const string FindToolName = "find_medarbejder";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly CultureInfo Danish = new("da-DK");
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd-MM-yyyy", "d-M-yyyy", "dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "d/M/yyyy"];

    private readonly IEmployeeService _employees;
    private readonly IPendingActionStore _pending;
    private readonly TurnContext _turn;
    private readonly ILogger<EmployeeTools> _logger;

    public EmployeeTools(
        IEmployeeService employees,
        IPendingActionStore pending,
        TurnContext turn,
        ILogger<EmployeeTools> logger)
    {
        _employees = employees;
        _pending = pending;
        _turn = turn;
        _logger = logger;
    }

    public string ToolName => CreateToolName;

    public IReadOnlyList<AITool> GetTools() =>
    [
        AIFunctionFactory.Create(PrepareCreateAsync, CreateToolName),
        AIFunctionFactory.Create(FindAsync, FindToolName),
    ];

    [Description(
        "Forbereder oprettelse af en ny medarbejder i HR-systemet PersonaleNet. Brug dette tool, når brugeren " +
        "beder dig om at oprette, tilføje eller ansætte en medarbejder. Alle fem oplysninger er påkrævede — mangler " +
        "en af dem, må du IKKE kalde toolet med gæt eller pladsholdere; spørg brugeren i stedet. Toolet opretter " +
        "ikke selv medarbejderen: det returnerer en opsummering, som du skal vise brugeren og bede om at bekræfte. " +
        "Brug det ikke til at forklare, hvordan man opretter en medarbejder — det er dokumentationssøgningens job.")]
    public async Task<string> PrepareCreateAsync(
        [Description("Medarbejderens fulde navn, f.eks. \"Lars Hansen\".")] string fuldeNavn,
        [Description("Medarbejderens e-mailadresse.")] string email,
        [Description("Afdelingen medarbejderen skal ansættes i, f.eks. \"Salg\".")] string afdeling,
        [Description("Stillingsbetegnelse, f.eks. \"Konsulent\".")] string stilling,
        [Description("Første arbejdsdag som yyyy-MM-dd, f.eks. \"2026-10-01\".")] string startdato,
        CancellationToken cancellationToken = default)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(fuldeNavn)) missing.Add("fulde navn");
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) missing.Add("gyldig e-mail");
        if (string.IsNullOrWhiteSpace(afdeling)) missing.Add("afdeling");
        if (string.IsNullOrWhiteSpace(stilling)) missing.Add("stilling");

        // Eksplicitte formater: "01-10-2026" er 1. oktober på dansk, men 10. januar for InvariantCulture.
        // Ambivalens her ville betyde en medarbejder med forkert startdato — derfor ingen "smart" parsing.
        DateOnly? start = null;
        if (string.IsNullOrWhiteSpace(startdato))
        {
            missing.Add("startdato");
        }
        else if (DateOnly.TryParseExact(startdato.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            start = parsed;
        }
        else
        {
            missing.Add("startdato i formatet yyyy-MM-dd");
        }

        if (missing.Count > 0)
        {
            return $"Kan ikke forberede oprettelsen — mangler: {string.Join(", ", missing)}. " +
                   "Spørg brugeren om de manglende oplysninger. Gæt ikke.";
        }

        var employee = new NewEmployee(fuldeNavn.Trim(), email.Trim(), afdeling.Trim(), stilling.Trim(), start!.Value);
        var summary = $"Opret {employee.FullName} ({employee.Email}) som {employee.JobTitle} i {employee.Department} " +
                      $"med start {employee.StartDate.ToString("d. MMMM yyyy", Danish)}";

        var action = new PendingAction(
            CreateToolName,
            summary,
            JsonSerializer.Serialize(employee, JsonOptions),
            DateTimeOffset.UtcNow);

        await _pending.SetAsync(_turn.ConversationId, action, cancellationToken);

        _logger.LogInformation(
            "Handling forberedt for samtale {ConversationId}: {Summary}",
            _turn.ConversationId,
            summary);

        return $"Handlingen er forberedt, men IKKE udført: {summary}. " +
               "Vis brugeren opsummeringen og spørg, om oplysningerne er korrekte, og om du skal udføre den. " +
               "Fortæl at brugeren kan svare 'ja' for at bekræfte, 'nej' for at annullere, eller rette oplysningerne.";
    }

    [Description(
        "Slår medarbejdere op i HR-systemet PersonaleNet efter navn (eller alle, hvis navnet udelades). " +
        "Brug det, når brugeren spørger, om en person er oprettet, eller vil se en medarbejders oplysninger. " +
        "Kræver ingen bekræftelse — det ændrer ikke noget.")]
    public async Task<string> FindAsync(
        [Description("Hele eller dele af navnet. Tom streng giver alle medarbejdere.")] string navn,
        CancellationToken cancellationToken = default)
    {
        var found = await _employees.FindAsync(string.IsNullOrWhiteSpace(navn) ? null : navn.Trim(), cancellationToken);
        if (found.Count == 0)
        {
            return string.IsNullOrWhiteSpace(navn)
                ? "Der er ingen medarbejdere i HR-systemet."
                : $"Ingen medarbejder matcher \"{navn}\" i HR-systemet.";
        }

        return $"Fandt {found.Count} medarbejder(e):\n" + string.Join(
            "\n",
            found.Select(e => $"- #{e.Id} {e.FullName}, {e.JobTitle} i {e.Department}, {e.Email}, start {e.StartDate:yyyy-MM-dd}"));
    }

    /// <summary>Kaldes af orkestratoren, når brugeren har bekræftet. Her sker den egentlige oprettelse.</summary>
    public async Task<string> ExecuteAsync(string parametersJson, CancellationToken cancellationToken = default)
    {
        var employee = JsonSerializer.Deserialize<NewEmployee>(parametersJson, JsonOptions)
            ?? throw new InvalidOperationException("Den ventende handling har ingen parametre.");

        try
        {
            var created = await _employees.CreateAsync(employee, cancellationToken);

            // Audit-spor (udbygges til rigtig audit-log i fase 5.1).
            _logger.LogInformation(
                "AUDIT tool={Tool} samtale={ConversationId} resultat=oprettet id={Id} navn={Name} email={Email}",
                CreateToolName, _turn.ConversationId, created.Id, created.FullName, created.Email);

            return $"{created.FullName} er nu oprettet i PersonaleNet som {created.JobTitle} i {created.Department} " +
                   $"med start {created.StartDate.ToString("d. MMMM yyyy", Danish)} (medarbejder-id {created.Id}). " +
                   "Brugerkonto og udstyr bestilles automatisk natten før startdatoen.";
        }
        catch (EmployeeServiceException ex)
        {
            _logger.LogWarning(
                "AUDIT tool={Tool} samtale={ConversationId} resultat=afvist fejl={Error}",
                CreateToolName, _turn.ConversationId, ex.Message);

            return $"HR-systemet afviste oprettelsen: {ex.Message} Ret oplysningerne, og prøv igen.";
        }
    }
}
