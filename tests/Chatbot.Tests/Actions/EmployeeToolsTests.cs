using Chatbot.Core.Actions;
using Chatbot.Core.Chat;
using Chatbot.Core.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chatbot.Tests.Actions;

public class EmployeeToolsTests
{
    private static (EmployeeTools Tools, FakeEmployeeService Hr, InMemoryPendingActionStore Pending) Create(string conversationId = "c1")
    {
        var hr = new FakeEmployeeService();
        var pending = new InMemoryPendingActionStore();
        var tools = new EmployeeTools(hr, pending, new TurnContext { ConversationId = conversationId }, NullLogger<EmployeeTools>.Instance);
        return (tools, hr, pending);
    }

    [Fact]
    public async Task PrepareCreate_med_alle_oplysninger_gemmer_ventende_handling_uden_at_oprette()
    {
        var (tools, hr, pending) = Create();

        var text = await tools.PrepareCreateAsync("Lars Hansen", "lars@firma.dk", "Salg", "Konsulent", "2026-10-01");

        Assert.Contains("IKKE udført", text);
        Assert.Contains("Lars Hansen", text);
        Assert.Empty(hr.Created);

        var action = await pending.GetAsync("c1");
        Assert.NotNull(action);
        Assert.Equal(EmployeeTools.CreateToolName, action.ToolName);
        Assert.Contains("lars@firma.dk", action.Summary);
    }

    [Theory]
    [InlineData("", "lars@firma.dk", "Salg", "Konsulent", "2026-10-01", "fulde navn")]
    [InlineData("Lars Hansen", "ikke-en-mail", "Salg", "Konsulent", "2026-10-01", "e-mail")]
    [InlineData("Lars Hansen", "lars@firma.dk", "", "Konsulent", "2026-10-01", "afdeling")]
    [InlineData("Lars Hansen", "lars@firma.dk", "Salg", "", "2026-10-01", "stilling")]
    [InlineData("Lars Hansen", "lars@firma.dk", "Salg", "Konsulent", "", "startdato")]
    [InlineData("Lars Hansen", "lars@firma.dk", "Salg", "Konsulent", "snart", "startdato")]
    public async Task PrepareCreate_med_manglende_oplysninger_beder_modellen_spoerge(
        string navn, string email, string afdeling, string stilling, string start, string expectedMissing)
    {
        var (tools, hr, pending) = Create();

        var text = await tools.PrepareCreateAsync(navn, email, afdeling, stilling, start);

        Assert.Contains("mangler", text);
        Assert.Contains(expectedMissing, text);
        Assert.Contains("Gæt ikke", text);
        Assert.Empty(hr.Created);
        Assert.Null(await pending.GetAsync("c1"));
    }

    [Fact]
    public async Task PrepareCreate_accepterer_dansk_datoformat()
    {
        var (tools, _, pending) = Create();

        await tools.PrepareCreateAsync("Lars Hansen", "lars@firma.dk", "Salg", "Konsulent", "01-10-2026");

        var action = await pending.GetAsync("c1");
        Assert.NotNull(action);
        Assert.Contains("1. oktober 2026", action.Summary);
    }

    [Fact]
    public async Task Execute_opretter_medarbejderen_ud_fra_de_gemte_parametre()
    {
        var (tools, hr, pending) = Create();
        await tools.PrepareCreateAsync("Lars Hansen", "lars@firma.dk", "Salg", "Konsulent", "2026-10-01");
        var action = (await pending.GetAsync("c1"))!;

        var reply = await tools.ExecuteAsync(action.ParametersJson);

        var created = Assert.Single(hr.Created);
        Assert.Equal("Lars Hansen", created.FullName);
        Assert.Equal(new DateOnly(2026, 10, 1), created.StartDate);
        Assert.Contains("er nu oprettet", reply);
        Assert.Contains("medarbejder-id 1", reply);
    }

    [Fact]
    public async Task Execute_formidler_afvisning_fra_HR_systemet_som_tekst()
    {
        var (tools, hr, pending) = Create();
        hr.FailWith = "En medarbejder med e-mailen lars@firma.dk findes allerede.";
        await tools.PrepareCreateAsync("Lars Hansen", "lars@firma.dk", "Salg", "Konsulent", "2026-10-01");
        var action = (await pending.GetAsync("c1"))!;

        var reply = await tools.ExecuteAsync(action.ParametersJson);

        Assert.Contains("afviste", reply);
        Assert.Contains("findes allerede", reply);
    }

    [Fact]
    public async Task Find_returnerer_medarbejdere_eller_tydelig_tom_besked()
    {
        var (tools, hr, _) = Create();
        Assert.Contains("ingen medarbejdere", await tools.FindAsync(""));

        await hr.CreateAsync(new NewEmployee("Lars Hansen", "lars@firma.dk", "Salg", "Konsulent", new DateOnly(2026, 10, 1)));

        Assert.Contains("Lars Hansen", await tools.FindAsync("lars"));
        Assert.Contains("Ingen medarbejder matcher", await tools.FindAsync("Mette"));
    }

    [Fact]
    public void GetTools_eksponerer_to_funktioner_med_beskrivelser()
    {
        var (tools, _, _) = Create();

        var functions = tools.GetTools().Cast<AIFunction>().ToList();

        Assert.Equal([EmployeeTools.CreateToolName, EmployeeTools.FindToolName], functions.Select(f => f.Name).ToArray());
        Assert.All(functions, f => Assert.False(string.IsNullOrWhiteSpace(f.Description)));
        Assert.Contains("startdato", functions[0].JsonSchema.ToString());
    }
}
