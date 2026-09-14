using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chatbot.Core.Tools;
using Chatbot.Core.Tools.Registry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chatbot.Infrastructure.Tools;

/// <summary>
/// Handler for tool-typen <c>http</c>: oversætter en tool-række til ét HTTP-kald. Alt står i
/// <c>handlerConfig</c>, så et nyt system kan kobles på uden kode:
/// <code>
/// {
///   "method": "POST",
///   "url": "http://localhost:5100/employees?name={navn}",   // {pladsholdere} = parametre fra modellen, URL-kodes
///   "headers": { "X-Api-Key": "…" },                         // valgfrit, statiske
///   "body": { "fullName": "{fuldeNavn}", "antal": "{antal}" }, // valgfrit; en værdi der KUN er en pladsholder beholder sin JSON-type
///   "timeoutSeconds": 15,
///   "successMessage": "{fullName} er oprettet (id {id})",   // 2xx-objekt: pladsholdere fra svaret + parametrene
///   "itemTemplate": "- {id} {fullName}",                     // 2xx-liste: én linje pr. element (successMessage bliver overskrift, {count} = antal)
///   "emptyMessage": "Ingen fundet.",                         // tom liste eller 404
///   "errorMessage": "Systemet afviste: {error}"              // 4xx: {error} = udtrukket fejltekst
/// }
/// </code>
/// Uden skabeloner returneres svarets rå tekst (klippet), hvilket modellerne som regel læser fint.
/// Fejl bliver til tekst, ikke undtagelser — modellen skal kunne forklare brugeren, hvad der gik galt.
/// </summary>
public sealed class HttpToolHandler : IToolHandler
{
    public const string HttpClientName = "tools";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> Methods = new(StringComparer.OrdinalIgnoreCase) { "GET", "POST", "PUT", "PATCH", "DELETE" };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly int _maxResultChars;
    private readonly ILogger<HttpToolHandler> _logger;

    public HttpToolHandler(IHttpClientFactory httpClientFactory, IOptions<ToolsOptions> options, ILogger<HttpToolHandler> logger)
    {
        _httpClientFactory = httpClientFactory;
        _maxResultChars = options.Value.Registry.MaxResultChars;
        _logger = logger;
    }

    public ToolHandlerType HandlerType => ToolHandlerType.Http;

    public IReadOnlyList<string> Validate(JsonElement handlerConfig)
    {
        var errors = new List<string>();
        HttpToolConfig? config;
        try
        {
            config = handlerConfig.ValueKind == JsonValueKind.Object ? handlerConfig.Deserialize<HttpToolConfig>(JsonOptions) : null;
        }
        catch (JsonException ex)
        {
            return [$"handlerConfig kunne ikke læses som HTTP-konfiguration: {ex.Message}"];
        }

        if (config is null)
        {
            return ["handlerConfig skal være et objekt med mindst \"method\" og \"url\"."];
        }

        if (string.IsNullOrWhiteSpace(config.Method) || !Methods.Contains(config.Method))
        {
            errors.Add("handlerConfig.method skal være GET, POST, PUT, PATCH eller DELETE.");
        }

        if (string.IsNullOrWhiteSpace(config.Url))
        {
            errors.Add("handlerConfig.url mangler.");
        }
        else
        {
            var probe = TemplateRenderer.Render(config.Url, new Dictionary<string, string>(), _ => "x");
            if (!Uri.TryCreate(probe, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                errors.Add("handlerConfig.url skal være en absolut http(s)-URL (pladsholdere som {navn} er tilladt).");
            }
        }

        if (config.Body is { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) } && string.Equals(config.Method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("handlerConfig.body kan ikke bruges med GET.");
        }

        if (config.TimeoutSeconds is < 1 or > 600)
        {
            errors.Add("handlerConfig.timeoutSeconds skal være mellem 1 og 600.");
        }

        return errors;
    }

    public async Task<string> InvokeAsync(ToolDefinition tool, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var config = tool.HandlerConfig.Deserialize<HttpToolConfig>(JsonOptions)
            ?? throw new InvalidOperationException($"Toolet '{tool.Name}' har ingen HTTP-konfiguration.");

        var values = TemplateRenderer.Flatten(arguments);
        var url = TemplateRenderer.Render(config.Url, values, Uri.EscapeDataString);
        var timeout = TimeSpan.FromSeconds(config.TimeoutSeconds ?? 30);

        using var request = new HttpRequestMessage(new HttpMethod(config.Method.ToUpperInvariant()), url);
        if (config.Headers is not null)
        {
            foreach (var (name, value) in config.Headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        if (config.Body is { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) } body)
        {
            var rendered = RenderJson(JsonNode.Parse(body.GetRawText()), arguments, values);
            request.Content = new StringContent(rendered?.ToJsonString(JsonOptions) ?? "null", Encoding.UTF8, "application/json");
        }

        _logger.LogInformation("HTTP-tool {Tool}: {Method} {Url}", tool.Name, request.Method, url);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        HttpResponseMessage response;
        try
        {
            response = await _httpClientFactory.CreateClient(HttpClientName).SendAsync(request, timeoutCts.Token);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "HTTP-tool {Tool} kunne ikke nå {Url}.", tool.Name, url);
            return $"Systemet bag '{tool.Name}' kunne ikke nås ({ex.Message}). " +
                   "Fortæl brugeren, at det ikke lykkedes lige nu, og at de kan prøve igen senere.";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("HTTP-tool {Tool} fik ikke svar fra {Url} inden {Timeout}.", tool.Name, url, timeout);
            return $"Systemet bag '{tool.Name}' svarede ikke inden for {timeout.TotalSeconds:0} sekunder. " +
                   "Fortæl brugeren, at det ikke lykkedes lige nu, og at de kan prøve igen senere.";
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            var status = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                return FormatSuccess(config, text, values);
            }

            if (response.StatusCode == HttpStatusCode.NotFound && config.EmptyMessage is not null)
            {
                return TemplateRenderer.Render(config.EmptyMessage, values);
            }

            if (status is >= 400 and < 500)
            {
                var error = ExtractError(text);
                _logger.LogInformation("HTTP-tool {Tool} afvist med {Status}: {Error}", tool.Name, status, error);

                if (config.ErrorMessage is not null)
                {
                    var withError = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase) { ["error"] = error, ["status"] = status.ToString() };
                    return TemplateRenderer.Render(config.ErrorMessage, withError);
                }

                return $"Systemet afviste kaldet (HTTP {status}): {error}";
            }

            _logger.LogWarning("HTTP-tool {Tool} fejlede med {Status}: {Body}", tool.Name, status, Truncate(text, 500));
            return $"Systemet bag '{tool.Name}' fejlede (HTTP {status}). " +
                   "Fortæl brugeren, at det ikke lykkedes lige nu, og at de kan prøve igen senere.";
        }
    }

    private string FormatSuccess(HttpToolConfig config, string body, IReadOnlyDictionary<string, string> values)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return config.SuccessMessage is not null ? TemplateRenderer.Render(config.SuccessMessage, values) : "Kaldet lykkedes.";
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return Truncate(body, _maxResultChars);
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                var count = root.GetArrayLength();
                if (count == 0)
                {
                    return config.EmptyMessage is not null ? TemplateRenderer.Render(config.EmptyMessage, values) : "Ingen resultater.";
                }

                if (config.ItemTemplate is null)
                {
                    return Truncate(body, _maxResultChars);
                }

                var sb = new StringBuilder();
                var header = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase) { ["count"] = count.ToString() };
                if (config.SuccessMessage is not null)
                {
                    sb.AppendLine(TemplateRenderer.Render(config.SuccessMessage, header));
                }

                foreach (var item in root.EnumerateArray())
                {
                    sb.AppendLine(TemplateRenderer.Render(config.ItemTemplate, Merge(values, item)));
                }

                return Truncate(sb.ToString().TrimEnd(), _maxResultChars);
            }

            if (config.SuccessMessage is not null)
            {
                return TemplateRenderer.Render(config.SuccessMessage, Merge(values, root));
            }

            return Truncate(body, _maxResultChars);
        }
    }

    /// <summary>Parametrene fra modellen + svarets top-felter (svaret vinder ved navnesammenfald).</summary>
    private static IReadOnlyDictionary<string, string> Merge(IReadOnlyDictionary<string, string> arguments, JsonElement response)
    {
        var merged = new Dictionary<string, string>(arguments, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in TemplateRenderer.Flatten(response))
        {
            merged[key] = value;
        }

        return merged;
    }

    /// <summary>
    /// Udfylder en body-skabelon. En strengværdi, der kun er én pladsholder ("{antal}"), erstattes af
    /// parameterens JSON-værdi, så tal forbliver tal; andre strenge rendres som tekst. Objekter og lister
    /// gennemløbes rekursivt.
    /// </summary>
    internal static JsonNode? RenderJson(JsonNode? template, JsonElement arguments, IReadOnlyDictionary<string, string> values)
    {
        switch (template)
        {
            case JsonObject obj:
            {
                var result = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    result[key] = RenderJson(value, arguments, values);
                }

                return result;
            }

            case JsonArray array:
                return new JsonArray(array.Select(item => RenderJson(item, arguments, values)).ToArray());

            case JsonValue value when value.TryGetValue<string>(out var text):
            {
                var placeholders = TemplateRenderer.Placeholders(text);
                if (placeholders.Count == 1 && text == "{" + placeholders[0] + "}"
                    && arguments.ValueKind == JsonValueKind.Object
                    && arguments.TryGetProperty(placeholders[0], out var raw))
                {
                    return JsonNode.Parse(raw.GetRawText());
                }

                return JsonValue.Create(TemplateRenderer.Render(text, values));
            }

            default:
                return template?.DeepClone();
        }
    }

    /// <summary>Fisker en læsbar fejltekst ud af typiske fejl-bodies: { "error": … }, { "errors": [...] }, ProblemDetails eller rå tekst.</summary>
    internal static string ExtractError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "ingen fejlbesked fra systemet.";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                {
                    return error.GetString()!;
                }

                if (root.TryGetProperty("errors", out var errors))
                {
                    if (errors.ValueKind == JsonValueKind.Array)
                    {
                        return string.Join(" ", errors.EnumerateArray().Select(TemplateRenderer.AsText));
                    }

                    if (errors.ValueKind == JsonValueKind.Object)
                    {
                        // ASP.NET's ValidationProblemDetails: { "errors": { "felt": ["besked"] } }
                        return string.Join(" ", errors.EnumerateObject()
                            .SelectMany(p => p.Value.ValueKind == JsonValueKind.Array ? p.Value.EnumerateArray().Select(TemplateRenderer.AsText) : [TemplateRenderer.AsText(p.Value)]));
                    }
                }

                var parts = new List<string>();
                if (root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String) parts.Add(title.GetString()!);
                if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String) parts.Add(detail.GetString()!);
                if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String) parts.Add(message.GetString()!);
                if (parts.Count > 0)
                {
                    return string.Join(" ", parts);
                }
            }
        }
        catch (JsonException)
        {
            // Ikke JSON — vis rå tekst.
        }

        return Truncate(body.Trim(), 500);
    }

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..max] + $"… [klippet, {text.Length - max} tegn udeladt]";

    /// <summary>Konfigurationen for et HTTP-tool, som den står i handler_config.</summary>
    internal sealed record HttpToolConfig(
        string Method,
        string Url,
        Dictionary<string, string>? Headers,
        JsonElement? Body,
        int? TimeoutSeconds,
        string? SuccessMessage,
        string? ItemTemplate,
        string? EmptyMessage,
        string? ErrorMessage);
}
