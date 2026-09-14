namespace Chatbot.Core.Actions;

/// <summary>
/// Adgangen til HR-systemet. I fase 3 er det dummy-API'et i <c>Chatbot.DummyHr</c>;
/// senere det rigtige system. Toolet kender kun dette interface.
/// </summary>
public interface IEmployeeService
{
    Task<EmployeeRecord> CreateAsync(NewEmployee employee, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmployeeRecord>> FindAsync(string? name, CancellationToken cancellationToken = default);
}

public sealed record NewEmployee(string FullName, string Email, string Department, string JobTitle, DateOnly StartDate);

public sealed record EmployeeRecord(int Id, string FullName, string Email, string Department, string JobTitle, DateOnly StartDate);

/// <summary>HR-systemet afviste handlingen (validering, dublet). Beskeden er egnet til at vise brugeren.</summary>
public sealed class EmployeeServiceException(string message) : Exception(message);
