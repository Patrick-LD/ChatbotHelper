using System.Collections.Concurrent;

namespace Chatbot.Core.Security;

/// <summary>
/// Hvad der skete med et tool: kaldt direkte (læse-tool), forberedt (skrive-tool venter på ja),
/// udført (brugeren sagde ja), annulleret (nej), udløbet, eller afvist (ingen rettighed ved udførelsen).
/// </summary>
public static class AuditKind
{
    public const string Called = "kaldt";
    public const string Prepared = "forberedt";
    public const string Executed = "udført";
    public const string Cancelled = "annulleret";
    public const string Expired = "udløbet";
    public const string Denied = "afvist";
    public const string Failed = "fejlet";
}

/// <summary>
/// Én linje i audit-loggen: hvem gjorde hvad, hvornår, med hvilke parametre — og hvad kom der ud af det.
/// Fase 3-4 skrev "AUDIT …" i applikationsloggen; fra fase 5.1 er det en tabel, der kan slås op.
/// </summary>
public sealed record AuditEntry(
    DateTimeOffset At,
    string UserId,
    IReadOnlyList<string> Roles,
    string ConversationId,
    string ToolName,
    string Kind,
    string ParametersJson,
    string Result,
    bool Success,
    long DurationMs)
{
    public long Id { get; init; }
}

public interface IAuditLog
{
    /// <summary>Skriver en linje. Må aldrig kaste ind i chat-flowet — en fejl i loggen logges og sluges.</summary>
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Seneste linjer, nyeste først. Valgfrit filtreret på bruger.</summary>
    Task<IReadOnlyList<AuditEntry>> ListAsync(int limit, string? userId = null, CancellationToken cancellationToken = default);
}

/// <summary>Audit-log i hukommelsen — til tests. Produktionen bruger PgAuditLog.</summary>
public sealed class InMemoryAuditLog : IAuditLog
{
    private readonly ConcurrentQueue<AuditEntry> _entries = new();
    private long _nextId;

    public IReadOnlyList<AuditEntry> Entries => _entries.ToList();

    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        _entries.Enqueue(entry with { Id = Interlocked.Increment(ref _nextId) });
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEntry>> ListAsync(int limit, string? userId = null, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<AuditEntry>>(_entries
            .Where(e => userId is null || e.UserId == userId)
            .OrderByDescending(e => e.Id)
            .Take(limit)
            .ToList());
}
