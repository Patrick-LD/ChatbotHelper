using System.Text.Json;
using Chatbot.Core.Rag;
using Chatbot.Core.Tools;
using Chatbot.Core.Tools.Registry;
using Chatbot.Infrastructure.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Chatbot.Tests.Tools;

public class ToolSeedTests
{
    private sealed class StubSearch : IInternalTool
    {
        public string Key => DocumentSearchTool.HandlerKey;

        public Task<string> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken = default) => Task.FromResult("ok");
    }

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new NotSupportedException();
    }

    private static IReadOnlyList<IToolHandler> Handlers() =>
    [
        new InternalToolHandler([new StubSearch()]),
        new HttpToolHandler(new NoHttp(), Options.Create(new ToolsOptions()), NullLogger<HttpToolHandler>.Instance),
    ];

    [Fact]
    public void Seedet_indeholder_fase_3s_tre_tools_og_alle_bestaar_valideringen()
    {
        var tools = ToolSeed.Default("http://localhost:5100/");

        Assert.Equal(["soeg_i_dokumentation", "find_medarbejder", "opret_medarbejder"], tools.Select(t => t.Name).ToArray());
        foreach (var tool in tools)
        {
            Assert.Empty(ToolDefinitionValidator.Validate(tool, Handlers()));
        }
    }

    [Fact]
    public void Kun_opret_medarbejder_kraever_bekraeftelse_og_er_forbeholdt_hr()
    {
        var tools = ToolSeed.Default("http://localhost:5100").ToDictionary(t => t.Name);

        Assert.True(tools["opret_medarbejder"].RequiresConfirmation);
        Assert.Equal([ToolSeed.RoleHr], tools["opret_medarbejder"].Roles);
        Assert.False(tools["find_medarbejder"].RequiresConfirmation);
        Assert.Equal([ToolSeed.RoleEmployee, ToolSeed.RoleHr], tools["find_medarbejder"].Roles);
        Assert.Equal([ToolSeed.RoleEmployee, ToolSeed.RoleHr], tools["soeg_i_dokumentation"].Roles);
    }

    [Fact]
    public void Http_url_bygges_af_den_konfigurerede_base_url_uden_dobbelt_skraastreg()
    {
        var find = ToolSeed.Default("http://hr.local:5100/").Single(t => t.Name == "find_medarbejder");

        Assert.Equal("http://hr.local:5100/employees?name={navn}", find.HandlerConfig.GetProperty("url").GetString());
    }

    [Theory]
    [InlineData("Opret_Medarbejder", "Navnet")]
    [InlineData("1tool", "Navnet")]
    public void Validator_afviser_ugyldige_navne(string name, string expectedError)
    {
        var tool = TestJson.Tool(name, roles: ["hr"]);

        Assert.Contains(ToolDefinitionValidator.Validate(tool, Handlers()), e => e.Contains(expectedError));
    }

    [Fact]
    public void Validator_kraever_summaryTemplate_med_kendte_parametre_for_skrive_tools()
    {
        var withoutSummary = TestJson.Tool("opret_x", requiresConfirmation: true, roles: ["hr"]);
        var withUnknown = TestJson.Tool("opret_y", requiresConfirmation: true, summary: "Opret {fuldeNavn}", roles: ["hr"]);

        Assert.Contains(ToolDefinitionValidator.Validate(withoutSummary, Handlers()), e => e.Contains("summaryTemplate"));
        Assert.Contains(ToolDefinitionValidator.Validate(withUnknown, Handlers()), e => e.Contains("{fuldeNavn}"));
    }

    [Fact]
    public void Validator_afviser_skema_der_ikke_er_et_objekt_og_required_uden_property()
    {
        var tool = TestJson.Tool("t", roles: ["hr"]) with
        {
            ParametersSchema = TestJson.Parse("""{ "type": "string", "required": ["x"] }"""),
        };

        var errors = ToolDefinitionValidator.Validate(tool, Handlers());

        Assert.Contains(errors, e => e.Contains("\"type\": \"object\""));
        Assert.Contains(errors, e => e.Contains("'x'"));
    }
}
