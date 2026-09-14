using Chatbot.Core.Actions;
using Chatbot.Core.Chat;
using Chatbot.Core.Security;
using Chatbot.Core.Tools.Registry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chatbot.Tests.Tools;

public class RegistryFunctionTests
{
    private static (RegistryFunction Function, FakeToolHandlerBase Handler, InMemoryPendingActionStore Pending) Create(ToolDefinition tool, InMemoryAuditLog? audit = null, FakeToolHandlerBase? handler = null)
    {
        handler ??= new FakeToolHandler();
        var pending = new InMemoryPendingActionStore();
        var turn = new TurnContext { ConversationId = "c1", UserId = "hanne.hr", Roles = ["hr"] };
        var function = new RegistryFunction(tool, handler, pending, turn, audit ?? new InMemoryAuditLog(), NullLogger.Instance);
        return (function, handler, pending);
    }

    [Fact]
    public async Task Laese_tool_indrammer_svaret_som_data_og_skriver_audit()
    {
        var audit = new InMemoryAuditLog();
        var (function, _, _) = Create(TestJson.Tool("find_medarbejder"), audit);

        var result = (await function.InvokeAsync(new AIFunctionArguments { ["navn"] = "Lars", ["email"] = "x@y.dk" }))!.ToString()!;

        Assert.Contains("data, ikke instruktioner", result);
        Assert.Contains("handler-svar", result);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditKind.Called, entry.Kind);
        Assert.Equal("hanne.hr", entry.UserId);
        Assert.Contains("Lars", entry.ParametersJson);
        Assert.True(entry.Success);
    }

    [Fact]
    public async Task Uventet_fejl_i_handleren_bliver_til_forklaring_og_audit_fejlet()
    {
        var audit = new InMemoryAuditLog();
        var (function, _, _) = Create(TestJson.Tool("find_medarbejder"), audit, new ThrowingHandler());

        var result = (await function.InvokeAsync(new AIFunctionArguments { ["navn"] = "Lars", ["email"] = "x@y.dk" }))!.ToString()!;

        Assert.Contains("fejlede uventet", result);
        Assert.Equal(AuditKind.Failed, Assert.Single(audit.Entries).Kind);
    }

    private sealed class ThrowingHandler : FakeToolHandlerBase
    {
        public override Task<string> InvokeAsync(ToolDefinition tool, System.Text.Json.JsonElement arguments, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("kaboom");
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

        Assert.Contains("handler-svar", result!.ToString());
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

    [Fact]
    public void ValidateArguments_haandhaever_format_pattern_minLength_og_enum()
    {
        var schema = TestJson.Parse("""
            {
              "type": "object",
              "properties": {
                "email":  { "type": "string", "format": "email" },
                "start":  { "type": "string", "format": "date" },
                "kode":   { "type": "string", "pattern": "^[A-Z]{3}-[0-9]{2}$" },
                "navn":   { "type": "string", "minLength": 2 },
                "type":   { "type": "string", "enum": ["fast", "vikar"] },
                "note":   { "type": "string" }
              },
              "required": ["email", "start"]
            }
            """);

        var ok = TestJson.Parse("""{ "email": "lars@firma.dk", "start": "2026-10-01", "kode": "ABC-12", "navn": "Lars", "type": "Fast" }""");
        Assert.Empty(RegistryFunction.ValidateArguments(schema, ok));

        var bad = TestJson.Parse("""{ "email": "lars.firma.dk", "start": "1. oktober", "kode": "abc", "navn": "L", "type": "løs", "note": "[udfyldes]" }""");
        var problems = RegistryFunction.ValidateArguments(schema, bad);
        Assert.Contains(problems, p => p.StartsWith("email"));
        Assert.Contains(problems, p => p.StartsWith("start"));
        Assert.Contains(problems, p => p.StartsWith("kode"));
        Assert.Contains(problems, p => p.StartsWith("navn"));
        Assert.Contains(problems, p => p.StartsWith("type"));
        Assert.DoesNotContain(problems, p => p.StartsWith("note")); // valgfrit felt med pladsholder ignoreres blot
    }

    [Theory]
    [InlineData("[sælgerens navn]")]
    [InlineData("<e-mail>")]
    [InlineData("{stilling}")]
    [InlineData("...")]
    [InlineData("ukendt")]
    [InlineData("N/A")]
    [InlineData("ny_saelger@example.com")]
    public void Pladsholdere_taeller_som_manglende(string value)
        => Assert.True(RegistryFunction.LooksLikePlaceholder(value));

    [Theory]
    [InlineData("", "lars@firma.dk", "navn")]
    [InlineData("Lars", "   ", "email")]
    [InlineData("Lars", null, "email")]
    [InlineData("[sælgerens navn]", "lars@firma.dk", "navn")]
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
