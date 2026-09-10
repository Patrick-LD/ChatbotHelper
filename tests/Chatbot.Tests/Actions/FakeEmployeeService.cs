using Chatbot.Core.Actions;

namespace Chatbot.Tests.Actions;

/// <summary>Står i stedet for HR-API'et i testene.</summary>
internal sealed class FakeEmployeeService : IEmployeeService
{
    public List<EmployeeRecord> Created { get; } = [];

    /// <summary>Sættes for at simulere, at HR-systemet afviser oprettelsen.</summary>
    public string? FailWith { get; set; }

    public Task<EmployeeRecord> CreateAsync(NewEmployee employee, CancellationToken cancellationToken = default)
    {
        if (FailWith is not null)
        {
            throw new EmployeeServiceException(FailWith);
        }

        var record = new EmployeeRecord(Created.Count + 1, employee.FullName, employee.Email, employee.Department, employee.JobTitle, employee.StartDate);
        Created.Add(record);
        return Task.FromResult(record);
    }

    public Task<IReadOnlyList<EmployeeRecord>> FindAsync(string? name, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<EmployeeRecord> result = Created
            .Where(e => name is null || e.FullName.Contains(name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return Task.FromResult(result);
    }
}
