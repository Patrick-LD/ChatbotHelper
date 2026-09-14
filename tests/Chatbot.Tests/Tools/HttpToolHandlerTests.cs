using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chatbot.Core.Tools;
using Chatbot.Core.Tools.Registry;
using Chatbot.Infrastructure.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Chatbot.Tests.Tools;

public class HttpToolHandlerTests
{
    /// <summary>Står i stedet for HR-API'et: husker sidste request og svarer det, testen har sat op.</summary>
    private sealed class FakeHttp : HttpMessageHandler, IHttpClientFactory
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public string Body { get; set; } = "{}";

        public Exception? Throw { get; set; }

        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastBody { get; private set; }

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            if (Throw is not null) throw Throw;
            return new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        }
    }

    private static (HttpToolHandler Handler, FakeHttp Http) Create()
    {
        var http = new FakeHttp();
        var handler = new HttpToolHandler(http, Options.Create(new ToolsOptions()), NullLogger<HttpToolHandler>.Instance);
        return (handler, http);
    }

    private static ToolDefinition Tool(string config) =>
        TestJson.Tool("t") with { HandlerConfig = TestJson.Parse(config) };

    [Fact]
    public async Task Post_udfylder_body_skabelonen_og_svarer_med_successMessage_fra_svaret()
    {
        var (handler, http) = Create();
        http.Status = HttpStatusCode.Created;
        http.Body = """{ "id": 7, "fullName": "Lars Hansen", "department": "Salg" }""";
        var tool = Tool("""
            {
              "method": "POST",
              "url": "http://hr/employees",
              "body": { "fullName": "{navn}", "email": "{email}", "note": "Oprettet af {navn}" },
              "successMessage": "{fullName} er nu oprettet i {department} (medarbejder-id {id})."
            }
            """);

        var reply = await handler.InvokeAsync(tool, TestJson.Parse("""{ "navn": "Lars Hansen", "email": "lars@firma.dk" }"""));

        Assert.Equal("Lars Hansen er nu oprettet i Salg (medarbejder-id 7).", reply);
        Assert.Equal(HttpMethod.Post, http.LastRequest!.Method);
        var sent = JsonNode.Parse(http.LastBody!)!;
        Assert.Equal("Lars Hansen", (string?)sent["fullName"]);
        Assert.Equal("lars@firma.dk", (string?)sent["email"]);
        Assert.Equal("Oprettet af Lars Hansen", (string?)sent["note"]);
    }

    [Fact]
    public async Task Get_url_koder_parametre_og_formaterer_lister_med_itemTemplate()
    {
        var (handler, http) = Create();
        http.Body = """[ { "id": 1, "fullName": "Lars Hansen" }, { "id": 2, "fullName": "Lars Holm" } ]""";
        var tool = Tool("""
            {
              "method": "GET",
              "url": "http://hr/employees?name={navn}",
              "successMessage": "Fandt {count} medarbejder(e):",
              "itemTemplate": "- #{id} {fullName}"
            }
            """);

        var reply = await handler.InvokeAsync(tool, TestJson.Parse("""{ "navn": "Lars H" }"""));

        Assert.Equal("http://hr/employees?name=Lars%20H", http.LastRequest!.RequestUri!.AbsoluteUri);
        Assert.Equal("Fandt 2 medarbejder(e):\r\n- #1 Lars Hansen\r\n- #2 Lars Holm".Replace("\r\n", Environment.NewLine), reply);
    }

    [Fact]
    public async Task Tom_liste_giver_emptyMessage()
    {
        var (handler, http) = Create();
        http.Body = "[]";
        var tool = Tool("""{ "method": "GET", "url": "http://hr/employees?name={navn}", "emptyMessage": "Ingen matcher \"{navn}\"." }""");

        Assert.Equal("Ingen matcher \"Mette\".", await handler.InvokeAsync(tool, TestJson.Parse("""{ "navn": "Mette" }""")));
    }

    [Fact]
    public async Task Afvisning_4xx_bliver_til_errorMessage_med_udtrukket_fejltekst()
    {
        var (handler, http) = Create();
        http.Status = HttpStatusCode.Conflict;
        http.Body = """{ "error": "En medarbejder med e-mailen lars@firma.dk findes allerede." }""";
        var tool = Tool("""{ "method": "POST", "url": "http://hr/employees", "body": {}, "errorMessage": "HR-systemet afviste oprettelsen: {error} Ret oplysningerne." }""");

        var reply = await handler.InvokeAsync(tool, TestJson.Parse("{}"));

        Assert.Equal("HR-systemet afviste oprettelsen: En medarbejder med e-mailen lars@firma.dk findes allerede. Ret oplysningerne.", reply);
    }

    [Fact]
    public async Task Uden_skabeloner_returneres_svarets_raa_tekst()
    {
        var (handler, http) = Create();
        http.Body = """{ "status": "ok", "employees": 3 }""";
        var tool = Tool("""{ "method": "GET", "url": "http://hr/health" }""");

        Assert.Equal("""{ "status": "ok", "employees": 3 }""", await handler.InvokeAsync(tool, TestJson.Parse("{}")));
    }

    [Fact]
    public async Task Systemet_kan_ikke_naas_giver_tekst_i_stedet_for_undtagelse()
    {
        var (handler, http) = Create();
        http.Throw = new HttpRequestException("Connection refused");
        var tool = Tool("""{ "method": "GET", "url": "http://hr/health" }""");

        var reply = await handler.InvokeAsync(tool, TestJson.Parse("{}"));

        Assert.Contains("kunne ikke nås", reply);
        Assert.Contains("Connection refused", reply);
    }

    [Fact]
    public async Task Serverfejl_5xx_bliver_til_en_forklaring()
    {
        var (handler, http) = Create();
        http.Status = HttpStatusCode.InternalServerError;
        http.Body = "kaboom";
        var tool = Tool("""{ "method": "GET", "url": "http://hr/health" }""");

        Assert.Contains("HTTP 500", await handler.InvokeAsync(tool, TestJson.Parse("{}")));
    }

    [Fact]
    public void RenderJson_beholder_json_typen_naar_vaerdien_kun_er_en_pladsholder()
    {
        var args = TestJson.Parse("""{ "antal": 3, "aktiv": true, "navn": "Lars" }""");
        var template = JsonNode.Parse("""{ "count": "{antal}", "on": "{aktiv}", "label": "Navn: {navn}", "fast": 1 }""");

        var rendered = HttpToolHandler.RenderJson(template, args, TemplateRenderer.Flatten(args))!.ToJsonString();

        Assert.Equal("""{"count":3,"on":true,"label":"Navn: Lars","fast":1}""", rendered);
    }

    [Theory]
    [InlineData("""{ "error": "dublet" }""", "dublet")]
    [InlineData("""{ "errors": ["a mangler.", "b mangler."] }""", "a mangler. b mangler.")]
    [InlineData("""{ "errors": { "startDate": ["Ugyldig dato."] } }""", "Ugyldig dato.")]
    [InlineData("""{ "title": "Bad Request", "detail": "Feltet er tomt." }""", "Bad Request Feltet er tomt.")]
    [InlineData("bare tekst", "bare tekst")]
    public void ExtractError_finder_en_laesbar_fejltekst(string body, string expected)
        => Assert.Equal(expected, HttpToolHandler.ExtractError(body));

    [Theory]
    [InlineData("""{ "method": "GET", "url": "http://hr/employees?name={navn}" }""", 0)]
    [InlineData("""{ "method": "FETCH", "url": "http://hr/x" }""", 1)]
    [InlineData("""{ "method": "GET", "url": "hr/x" }""", 1)]
    [InlineData("""{ "method": "GET", "url": "http://hr/x", "body": { "a": 1 } }""", 1)]
    [InlineData("""{ "url": "http://hr/x" }""", 1)]
    public void Validate_fanger_typiske_konfigurationsfejl(string config, int expectedErrors)
    {
        var (handler, _) = Create();

        Assert.Equal(expectedErrors, handler.Validate(TestJson.Parse(config)).Count);
    }
}
