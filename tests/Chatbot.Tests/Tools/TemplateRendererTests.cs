using System.Text.Json;
using Chatbot.Core.Tools.Registry;

namespace Chatbot.Tests.Tools;

public class TemplateRendererTests
{
    [Fact]
    public void Render_erstatter_pladsholdere_og_lader_ukendte_blive_tomme()
    {
        var values = new Dictionary<string, string> { ["navn"] = "Lars", ["afdeling"] = "Salg" };

        Assert.Equal("Opret Lars i Salg ()", TemplateRenderer.Render("Opret {navn} i {afdeling} ({ukendt})", values));
    }

    [Fact]
    public void Render_kan_transformere_vaerdier_til_fx_url_kodning()
    {
        var values = new Dictionary<string, string> { ["navn"] = "Lars Hansen" };

        Assert.Equal("/employees?name=Lars%20Hansen", TemplateRenderer.Render("/employees?name={navn}", values, Uri.EscapeDataString));
    }

    [Fact]
    public void Flatten_giver_tal_og_bool_uden_anfoerselstegn_og_er_case_insensitiv()
    {
        using var json = JsonDocument.Parse("""{ "id": 7, "ok": true, "navn": "Lars", "intet": null }""");

        var values = TemplateRenderer.Flatten(json.RootElement);

        Assert.Equal("7", values["id"]);
        Assert.Equal("true", values["ok"]);
        Assert.Equal("Lars", values["NAVN"]);
        Assert.Equal("", values["intet"]);
    }

    [Fact]
    public void Placeholders_finder_navnene_uden_dubletter()
    {
        Assert.Equal(["a", "b"], TemplateRenderer.Placeholders("{a} og {b} og {a}"));
        Assert.False(TemplateRenderer.HasPlaceholders("ingen her"));
    }
}
