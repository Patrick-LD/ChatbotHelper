using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

// Evalueringsværktøj: kører evalueringssættet mod et kørende API og skriver resultatet som Markdown.
//
//   dotnet run --project tools/Chatbot.Eval -- docs/evaluering/evalueringssaet.json docs/evaluering/resultater/<dato>-<navn>.md
//
// Hvert spørgsmål stilles i en ny samtale. Et svar er "korrekt", når alle forventede nøgleord
// findes i svaret (alternativer adskilles med |), ingen forbudte nøgleord findes, og den forventede
// kilde optræder blandt de kilder, botten slog op. Det er en grov, automatisk vurdering — men den
// er ens fra kørsel til kørsel, og det er det, der gør den brugbar som baseline.
//
// Fase 3: en case kan have "turns" (flere beskeder i samme samtale) i stedet for "question";
// forventningerne gælder det sidste svar. "expectedPending" tjekker, om en handling venter på
// bekræftelse efter sidste tur. HR-dummy'en nulstilles før kørslen (CHATBOT_HR_URL), så
// oprettelser fra en tidligere kørsel ikke giver dublet-afvisninger.

var setPath = args.Length > 0 ? args[0] : "docs/evaluering/evalueringssaet.json";
var outputPath = args.Length > 1 ? args[1] : null;
var baseUrl = Environment.GetEnvironmentVariable("CHATBOT_URL") ?? "http://localhost:5022";

var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
};

var set = JsonSerializer.Deserialize<EvalSet>(await File.ReadAllTextAsync(setPath), jsonOptions)
    ?? throw new InvalidOperationException($"Kunne ikke læse {setPath}.");

using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromMinutes(10) };

var hrUrl = Environment.GetEnvironmentVariable("CHATBOT_HR_URL") ?? "http://localhost:5100";
try
{
    using var reset = await http.DeleteAsync(new Uri(new Uri(hrUrl), "/employees"));
    Console.WriteLine($"HR-dummy nulstillet ({(int)reset.StatusCode}) på {hrUrl}");
}
catch (Exception ex)
{
    Console.WriteLine($"Advarsel: kunne ikke nulstille HR-dummy på {hrUrl}: {ex.Message}");
}

Console.WriteLine($"Evaluering: {set.Cases.Count} spørgsmål mod {baseUrl}");
Console.WriteLine();

var results = new List<CaseResult>();
var total = Stopwatch.StartNew();

foreach (var c in set.Cases)
{
    var sw = Stopwatch.StartNew();
    ChatResponse? response = null;
    string? error = null;
    string? conversationId = null;

    var turns = c.Turns is { Count: > 0 } ? c.Turns : [c.Question ?? string.Empty];
    foreach (var turn in turns)
    {
        try
        {
            using var reply = await http.PostAsJsonAsync("/chat", new { message = turn, conversationId });
            if (!reply.IsSuccessStatusCode)
            {
                error = $"HTTP {(int)reply.StatusCode}: {await reply.Content.ReadAsStringAsync()}";
                break;
            }

            response = await reply.Content.ReadFromJsonAsync<ChatResponse>(jsonOptions);
            conversationId = response?.ConversationId;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            break;
        }
    }

    sw.Stop();

    var text = response?.Reply ?? string.Empty;
    var missing = (c.Expected ?? []).Where(k => !ContainsAny(text, k)).ToList();
    var forbidden = (c.Forbidden ?? []).Where(k => ContainsAny(text, k)).ToList();
    var sources = response?.Sources.Select(s => s.Source).Distinct().ToList() ?? [];
    var sourceOk = c.ExpectedSource is null
        ? true
        : c.ExpectedSource == string.Empty ? sources.Count == 0 : c.ExpectedSource.Split('|').Any(sources.Contains);

    var pendingOk = c.ExpectedPending is null || c.ExpectedPending == (response?.PendingAction is not null);

    var passed = error is null && missing.Count == 0 && forbidden.Count == 0 && sourceOk && pendingOk;
    results.Add(new CaseResult(c, response, error, missing, forbidden, sourceOk, pendingOk, passed, sw.Elapsed));

    Console.WriteLine($"{(passed ? "✅" : "❌")} {c.Id,-4} {c.Title}");
    if (!passed)
    {
        if (error is not null) Console.WriteLine($"        fejl: {error}");
        if (missing.Count > 0) Console.WriteLine($"        mangler: {string.Join(", ", missing)}");
        if (forbidden.Count > 0) Console.WriteLine($"        forbudt: {string.Join(", ", forbidden)}");
        if (!sourceOk) Console.WriteLine($"        kilde: forventede '{c.ExpectedSource}', fik [{string.Join(", ", sources)}]");
        if (!pendingOk) Console.WriteLine($"        ventende handling: forventede {c.ExpectedPending}, fik '{response?.PendingAction}'");
    }
}

total.Stop();

var passedCount = results.Count(r => r.Passed);
var percent = results.Count == 0 ? 0 : 100.0 * passedCount / results.Count;

Console.WriteLine();
Console.WriteLine($"Resultat: {passedCount}/{results.Count} korrekte ({percent:0} %) på {total.Elapsed.TotalSeconds:0} s.");

if (outputPath is not null)
{
    var report = BuildReport(set, results, percent, total.Elapsed, baseUrl);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
    await File.WriteAllTextAsync(outputPath, report, new UTF8Encoding(false));
    Console.WriteLine($"Rapport skrevet til {outputPath}");
}

return passedCount == results.Count ? 0 : 1;

static bool ContainsAny(string text, string alternatives)
    => alternatives.Split('|').Any(a => text.Contains(a.Trim(), StringComparison.OrdinalIgnoreCase));

static string BuildReport(EvalSet set, List<CaseResult> results, double percent, TimeSpan elapsed, string baseUrl)
{
    var sb = new StringBuilder();
    sb.AppendLine($"# Evaluering — {DateTime.Now:yyyy-MM-dd HH:mm}");
    sb.AppendLine();
    sb.AppendLine($"- **Resultat:** {results.Count(r => r.Passed)}/{results.Count} korrekte ({percent:0} %)");
    sb.AppendLine($"- **Grønt lys (≥ 80 %):** {(percent >= 80 ? "✅ ja" : "⛔ nej")}");
    sb.AppendLine($"- **Samlet tid:** {elapsed.TotalSeconds:0} s ({elapsed.TotalSeconds / Math.Max(1, results.Count):0.0} s pr. spørgsmål)");
    sb.AppendLine($"- **API:** {baseUrl}");
    if (!string.IsNullOrWhiteSpace(set.Notes))
    {
        sb.AppendLine($"- **Noter til sættet:** {set.Notes}");
    }

    sb.AppendLine();
    sb.AppendLine("Opsætning (model, chunking, TopK, MinScore) noteres manuelt herunder, så kørsler kan sammenlignes:");
    sb.AppendLine();
    sb.AppendLine("> _Udfyld: chatmodel, embedding-model, ChunkSize/ChunkOverlap, TopK, MinScore, hvad der blev ændret siden sidst._");
    sb.AppendLine();
    sb.AppendLine("| # | Spørgsmål | Resultat | Mangler | Forbudt | Kilde ok | Bekræft ok | Tid |");
    sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
    foreach (var r in results)
    {
        sb.AppendLine(
            $"| {r.Case.Id} | {Escape(r.Case.Title)} | {(r.Passed ? "✅" : "❌")} | " +
            $"{Escape(string.Join(", ", r.Missing))} | {Escape(string.Join(", ", r.Forbidden))} | " +
            $"{(r.SourceOk ? "✅" : "❌")} | {(r.PendingOk ? "✅" : "❌")} | {r.Elapsed.TotalSeconds:0.0} s |");
    }

    sb.AppendLine();
    sb.AppendLine("## Svar");
    sb.AppendLine();
    foreach (var r in results)
    {
        sb.AppendLine($"### {r.Case.Id} — {r.Case.Title}");
        sb.AppendLine();
        if (r.Case.Turns is { Count: > 1 })
        {
            sb.AppendLine("**Ture:** " + string.Join(" → ", r.Case.Turns.Select(t => "\"" + t + "\"")));
            sb.AppendLine();
        }

        sb.AppendLine($"**Facit:** {r.Case.Answer}");
        sb.AppendLine();
        if (r.Error is not null)
        {
            sb.AppendLine($"**Fejl:** `{r.Error}`");
        }
        else
        {
            sb.AppendLine("**Botten svarede:**");
            sb.AppendLine();
            foreach (var line in (r.Response?.Reply ?? string.Empty).Split('\n'))
            {
                sb.AppendLine($"> {line.TrimEnd()}");
            }

            sb.AppendLine();
            var sources = r.Response?.Sources ?? [];
            sb.AppendLine(sources.Count == 0
                ? "**Kilder:** ingen (botten søgte ikke, eller intet lå over MinScore)"
                : "**Kilder:** " + string.Join("; ", sources.Select(s => $"{s.Source} › {s.Heading} ({s.Score:0.00})")));
            if (r.Response?.PendingAction is not null)
            {
                sb.AppendLine();
                sb.AppendLine($"**Venter på bekræftelse:** {r.Response.PendingAction}");
            }
        }

        sb.AppendLine();
    }

    return sb.ToString();
}

static string Escape(string s) => s.Replace("|", "\\|").Replace("\n", " ");

sealed record EvalSet(string? Notes, List<EvalCase> Cases);

/// <param name="Id">Kort id, f.eks. "Q01".</param>
/// <param name="Question">Spørgsmålet som en bruger ville stille det.</param>
/// <param name="Answer">Facit i prosa — til mennesket der læser rapporten.</param>
/// <param name="Expected">Nøgleord der SKAL være i svaret. "a|b" betyder a eller b.</param>
/// <param name="Forbidden">Nøgleord der IKKE må være i svaret (typisk hallucinationer).</param>
/// <param name="ExpectedSource">Kildefil der skal være slået op. Tom streng = botten må ikke have fundet noget. null = ligegyldigt.</param>
sealed record EvalCase(
    string Id,
    string? Question,
    List<string>? Turns,
    string Answer,
    List<string>? Expected,
    List<string>? Forbidden,
    string? ExpectedSource,
    bool? ExpectedPending)
{
    /// <summary>Overskrift i rapporten: spørgsmålet, eller første tur for flertrins-cases.</summary>
    public string Title => Question ?? (Turns is { Count: > 0 } ? Turns[0] + (Turns.Count > 1 ? $" (+{Turns.Count - 1} ture)" : "") : Id);
}

sealed record ChatResponse(string Reply, string ConversationId, List<SourceDto> Sources, string? PendingAction);

sealed record SourceDto(string Source, string Heading, double Score);

sealed record CaseResult(
    EvalCase Case,
    ChatResponse? Response,
    string? Error,
    List<string> Missing,
    List<string> Forbidden,
    bool SourceOk,
    bool PendingOk,
    bool Passed,
    TimeSpan Elapsed);
