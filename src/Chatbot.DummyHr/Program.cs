using System.Collections.Concurrent;

// Dummy-HR-API (fase 3.1): et lille "PersonaleNet" i hukommelsen, så botten har et rigtigt
// HTTP-endpoint at kalde uden risiko. Kører separat fra chatbotten på port 5100, fordi det
// er sådan, virkelige tools ser ud i fase 4: eksterne systemer bag HTTP.
//
//   dotnet run --project src/Chatbot.DummyHr
//
// Data forsvinder ved genstart — det er meningen.

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var employees = new ConcurrentDictionary<int, Employee>();
var nextId = 0;

app.MapGet("/health", () => Results.Ok(new { status = "ok", employees = employees.Count }));

app.MapGet("/employees", (string? name) =>
{
    var query = employees.Values.AsEnumerable();
    if (!string.IsNullOrWhiteSpace(name))
    {
        query = query.Where(e => e.FullName.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    return Results.Ok(query.OrderBy(e => e.Id).ToList());
});

app.MapGet("/employees/{id:int}", (int id) =>
    employees.TryGetValue(id, out var e) ? Results.Ok(e) : Results.NotFound());

app.MapPost("/employees", (CreateEmployeeRequest request) =>
{
    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(request.FullName)) errors.Add("fullName mangler.");
    if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@')) errors.Add("email mangler eller er ugyldig.");
    if (string.IsNullOrWhiteSpace(request.Department)) errors.Add("department mangler.");
    if (string.IsNullOrWhiteSpace(request.JobTitle)) errors.Add("jobTitle mangler.");
    if (request.StartDate is null) errors.Add("startDate mangler (yyyy-MM-dd).");

    if (errors.Count > 0)
    {
        return Results.BadRequest(new { errors });
    }

    if (employees.Values.Any(e => e.Email.Equals(request.Email, StringComparison.OrdinalIgnoreCase)))
    {
        return Results.Conflict(new { error = $"En medarbejder med e-mailen {request.Email} findes allerede." });
    }

    var id = Interlocked.Increment(ref nextId);
    var employee = new Employee(
        id,
        request.FullName!.Trim(),
        request.Email!.Trim(),
        request.Department!.Trim(),
        request.JobTitle!.Trim(),
        request.StartDate!.Value,
        DateTimeOffset.UtcNow);

    employees[id] = employee;
    app.Logger.LogInformation("Oprettede medarbejder {Id}: {Name} ({Email})", id, employee.FullName, employee.Email);

    return Results.Created($"/employees/{id}", employee);
});

app.MapDelete("/employees", () =>
{
    employees.Clear();
    return Results.NoContent();
})
.WithDescription("Nulstiller data — bruges af evalueringen.");

app.Run();

public sealed record CreateEmployeeRequest(string? FullName, string? Email, string? Department, string? JobTitle, DateOnly? StartDate);

public sealed record Employee(int Id, string FullName, string Email, string Department, string JobTitle, DateOnly StartDate, DateTimeOffset CreatedAt);
