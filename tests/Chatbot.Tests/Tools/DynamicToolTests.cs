using Chatbot.Core.Actions;
using Chatbot.Core.Chat;
using Chatbot.Core.Security;
using Chatbot.Core.Tools.Registry;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chatbot.Tests.Tools;

public class DynamicToolProviderTests
{
    private static FakeToolRegistry Registry()
    {
        var registry = new FakeToolRegistry();
        registry.Tools.Add(TestJson.Tool("soeg_i_dokumentation", roles: ["medarbejder", "hr"]));
        registry.Tools.Add(TestJson.Tool("find_medarbejder", roles: ["medarbejder", "hr"]));
        registry.Tools.Add(TestJson.Tool("opret_medarbejder", requiresConfirmation: true, summary: "Opret {navn}", roles: ["hr"]));
        registry.Tools.Add(TestJson.Tool("gammelt_tool", isActive: false, roles: ["medarbejder", "hr"]));
        return registry;
    }

    private static DynamicToolProvider Provider(FakeToolRegistry registry, params string[] roles)
        => new(registry, [new FakeToolHandler()], new InMemoryPendingActionStore(), new TurnContext { Roles = roles }, new InMemoryAuditLog(), NullLoggerFactory.Instance);

    [Fact]
    public async Task Medarbejder_ser_kun_laese_tools()
    {
        var tools = await Provider(Registry(), "medarbejder").GetToolsAsync();

        Assert.Equal(["find_medarbejder", "soeg_i_dokumentation"], tools.Select(t => t.Name).ToArray());
    }

    [Fact]
    public async Task Hr_ser_ogsaa_opret_medarbejder_men_aldrig_inaktive_tools()
    {
        var tools = await Provider(Registry(), "hr").GetToolsAsync();

        Assert.Equal(["find_medarbejder", "opret_medarbejder", "soeg_i_dokumentation"], tools.Select(t => t.Name).ToArray());
    }

    [Fact]
    public async Task Uden_roller_faar_modellen_ingen_tools()
    {
        Assert.Empty(await Provider(Registry()).GetToolsAsync());
    }

    [Fact]
    public async Task Tools_uden_handler_for_typen_springes_over()
    {
        var registry = new FakeToolRegistry();
        registry.Tools.Add(TestJson.Tool("mcp_tool", type: ToolHandlerType.Mcp, roles: ["hr"]));
        registry.Tools.Add(TestJson.Tool("http_tool", type: ToolHandlerType.Http, roles: ["hr"]));

        var tools = await Provider(registry, "hr").GetToolsAsync();

        Assert.Equal(["http_tool"], tools.Select(t => t.Name).ToArray());
    }

    [Fact]
    public async Task Database_nede_giver_ingen_tools_men_ingen_undtagelse()
    {
        var registry = Registry();
        registry.FailWith = new InvalidOperationException("Postgres svarer ikke");

        Assert.Empty(await Provider(registry, "hr").GetToolsAsync());
    }
}

public class DynamicActionExecutorTests
{
    private static (DynamicActionExecutor Executor, FakeToolHandler Handler, InMemoryAuditLog Audit) Create(FakeToolRegistry registry, params string[] roles)
    {
        var handler = new FakeToolHandler(reply: "Lars er nu oprettet (id 1).");
        var audit = new InMemoryAuditLog();
        var turn = new TurnContext { ConversationId = "c1", UserId = "hanne.hr", Roles = roles };
        var executor = new DynamicActionExecutor(registry, [handler], turn, audit, NullLogger<DynamicActionExecutor>.Instance);
        return (executor, handler, audit);
    }

    private static PendingAction Action() => new("opret_medarbejder", "Opret Lars", """{"navn":"Lars","email":"lars@firma.dk"}""", DateTimeOffset.UtcNow);

    [Fact]
    public async Task Udfoerer_via_handleren_med_de_gemte_parametre()
    {
        var registry = new FakeToolRegistry();
        registry.Tools.Add(TestJson.Tool("opret_medarbejder", requiresConfirmation: true, summary: "Opret {navn}", roles: ["hr"]));
        var (executor, handler, audit) = Create(registry, "hr");

        Assert.True(await executor.CanExecuteAsync("opret_medarbejder"));
        var reply = await executor.ExecuteAsync(Action());

        Assert.Equal("Lars er nu oprettet (id 1).", reply);
        var call = Assert.Single(handler.Calls);
        Assert.Equal("lars@firma.dk", call.Arguments.GetProperty("email").GetString());

        // Audit-loggen (fase 5.1): hvem, hvad, med hvilke parametre og hvad kom der ud af det.
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditKind.Executed, entry.Kind);
        Assert.Equal("hanne.hr", entry.UserId);
        Assert.Equal(["hr"], entry.Roles);
        Assert.Equal("opret_medarbejder", entry.ToolName);
        Assert.Contains("lars@firma.dk", entry.ParametersJson);
        Assert.Equal("Lars er nu oprettet (id 1).", entry.Result);
    }

    [Fact]
    public async Task Afviser_naar_turens_roller_ikke_laengere_giver_adgang()
    {
        var registry = new FakeToolRegistry();
        registry.Tools.Add(TestJson.Tool("opret_medarbejder", requiresConfirmation: true, summary: "Opret {navn}", roles: ["hr"]));
        var (executor, handler, audit) = Create(registry, "medarbejder");

        var reply = await executor.ExecuteAsync(Action());

        Assert.Contains("Intet er ændret", reply);
        Assert.Empty(handler.Calls);
        Assert.Equal(AuditKind.Denied, Assert.Single(audit.Entries).Kind);
    }

    [Fact]
    public async Task Kan_ikke_udfoere_tools_der_er_slettet_fra_registret()
    {
        var (executor, _, _) = Create(new FakeToolRegistry(), "hr");

        Assert.False(await executor.CanExecuteAsync("opret_medarbejder"));
    }
}

public class InternalToolHandlerTests
{
    private sealed class EchoTool : IInternalTool
    {
        public string Key => "ekko";

        public Task<string> InvokeAsync(System.Text.Json.JsonElement arguments, CancellationToken cancellationToken = default)
            => Task.FromResult("ekko: " + arguments.GetProperty("tekst").GetString());
    }

    [Fact]
    public async Task Kalder_det_interne_tool_der_matcher_noeglen()
    {
        var handler = new InternalToolHandler([new EchoTool()]);
        var tool = TestJson.Tool("ekko_tool", type: ToolHandlerType.Internal) with { HandlerConfig = TestJson.Parse("""{ "handler": "ekko" }""") };

        Assert.Empty(handler.Validate(tool.HandlerConfig));
        Assert.Equal("ekko: hej", await handler.InvokeAsync(tool, TestJson.Parse("""{ "tekst": "hej" }""")));
    }

    [Fact]
    public async Task Ukendt_noegle_afvises_ved_validering_og_giver_tekst_ved_kald()
    {
        var handler = new InternalToolHandler([new EchoTool()]);
        var tool = TestJson.Tool("x", type: ToolHandlerType.Internal) with { HandlerConfig = TestJson.Parse("""{ "handler": "findes_ikke" }""") };

        var errors = handler.Validate(tool.HandlerConfig);
        Assert.Contains(errors, e => e.Contains("findes_ikke") && e.Contains("ekko"));
        Assert.Contains("ikke findes", await handler.InvokeAsync(tool, TestJson.Parse("{}")));
    }
}
