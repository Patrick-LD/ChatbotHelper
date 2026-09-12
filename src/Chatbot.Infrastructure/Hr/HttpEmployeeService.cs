using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Chatbot.Core.Actions;

namespace Chatbot.Infrastructure.Hr;

/// <summary>
/// <see cref="IEmployeeService"/> mod HR-API'et over HTTP. Registreres med en typed HttpClient,
/// hvis BaseAddress peger på dummy-API'et (fase 3) og senere på det rigtige system.
/// </summary>
public sealed class HttpEmployeeService : IEmployeeService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public HttpEmployeeService(HttpClient http) => _http = http;

    public async Task<EmployeeRecord> CreateAsync(NewEmployee employee, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync("/employees", employee, JsonOptions, cancellationToken);

        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new EmployeeServiceException(ExtractMessage(body));
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<EmployeeRecord>(JsonOptions, cancellationToken)
            ?? throw new EmployeeServiceException("HR-systemet svarede uden data.");
    }

    public async Task<IReadOnlyList<EmployeeRecord>> FindAsync(string? name, CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrWhiteSpace(name) ? "/employees" : $"/employees?name={Uri.EscapeDataString(name)}";
        return await _http.GetFromJsonAsync<List<EmployeeRecord>>(url, JsonOptions, cancellationToken) ?? [];
    }

    private static string ExtractMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                return error.GetString() ?? body;
            }

            if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
            {
                return string.Join(" ", errors.EnumerateArray().Select(e => e.GetString()));
            }
        }
        catch (JsonException)
        {
            // Ikke JSON — vis rå tekst.
        }

        return body;
    }
}
