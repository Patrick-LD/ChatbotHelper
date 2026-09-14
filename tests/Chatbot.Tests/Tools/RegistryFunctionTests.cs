using Chatbot.Core.Actions;
using Chatbot.Core.Chat;
using Chatbot.Core.Tools.Registry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chatbot.Tests.Tools;

public class RegistryFunctionTests
{
    private static (RegistryFunction Function, FakeToolHandler Handler, InMemoryPendingActionStore Pending) Create(ToolDefinition tool)
    {
        var handler = new FakeToolHandler();
        var pending = new InMemoryPendingActionStore();
        var function = new RegistryFunction(tool, handler, pending, new TurnContext { ConversationId = "c1" }, NullLogger.Instance);
        return (function, handler, pending);
    }

    [Fact]
    public void Eksponerer_navn_beskrivelse_og_skema_fra_definitionen()
    {
        var (function, _, _) = Create(TestJson.Tool("find_medarbejder"));

        Assert.Equal("find_medarbejder", function.Name);
        Assert.Equal("Beskrivelse af find_medarbejder", function.Description);
        Assert.Contains("\"email\"", function.JsonSchema.GetRawText());
    }

    [Fact]
    public async Task Laese_tool_udfoeres_straks_og_argumenter_uden_for_skemaet_fjernes()
    {
        var (function, handler, pending) = Create(TestJson.Tool("find_medarbejder"));

        var result = await function.InvokeAsync(new AIFunctionArguments
        {
            ["navn"] = "Lars",
            ["email"] = "lars@firma.dk",
            ["smuglet"] = "ekstra",
        });

        Assert.Equal("handler-svar", result);
        var call = Assert.Single(handler.Calls);
        Assert.Equal("Lars", call.Arguments.GetProperty("navn").GetString());
        Assert.False(call.Arguments.TryGetProperty("smuglet", out _));
        Assert.Null(await pending.GetAsync("c1"));
    }

    [Fact]
    public async Task Skrive_tool_gemmer_ventende_handling_og_kalder_ikke_handleren()
    {
        var (function, handler, pending) = Create(TestJson.Tool("opret_medarbejder", requiresConfirmation: true, summary: "Opret {navn} ({email})"));

        var result = await function.InvokeAsync(new AIFunctionArguments { ["navn"] = "Lars Hansen", ["email"] = "lars@firma.dk" });

        Assert.Contains("IKKE udført", result!.ToString());
        Assert.Empty(handler.Calls);

        var action = await pending.GetAsync("c1");
        Assert.NotNull(action);
        Assert.Equal("opret_medarbejder", action.ToolName);
        Assert.Equal("Opret Lars Hansen (lars@firma.dk)", action.Summary);
        Assert.Contains("lars@firma.dk", action.ParametersJson);
    }

    [Theory]
    [InlineData("", "lars@firma.dk", "navn")]
    [InlineData("Lars", "   ", "email")]
    [InlineData("Lars", null, "email")]
    public async Task Skrive_tool_med_manglende_paakraevede_felter_beder_modellen_spoerge(string navn, string? email, string expectedMissing)
    {
        var (function, handler, pending) = Create(TestJson.Tool("opret_medarbejder", requiresConfirmation: true, summary: "Opret {navn}"));

        var args = new AIFunctionArguments { ["navn"] = navn };
        if (email is not null) args["email"] = email;

        var result = (await function.InvokeAsync(args))!.ToString()!;

        Assert.Contains("mangler", result);
        Assert.Contains(expectedMissing, result);
        Assert.Contains("Gæt ikke", result);
        Assert.Empty(handler.Calls);
        Assert.Null(await pending.GetAsync("c1"));
    }
}
