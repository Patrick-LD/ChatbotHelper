using Chatbot.Infrastructure.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Chatbot.Infrastructure.Chat;

/// <summary>
/// Opretter tabellerne til chathistorik, ventende handlinger og audit-log ved opstart (fase 5).
/// Samme holdning som de øvrige initializers: fejler databasen, starter API'et alligevel — men
/// uden database kan chatten hverken huske samtaler eller skrive audit, og det står i loggen.
/// </summary>
public sealed class ChatStoreInitializer : IHostedService
{
    private readonly PgConversationStore _conversations;
    private readonly PgPendingActionStore _pending;
    private readonly PgAuditLog _audit;
    private readonly ILogger<ChatStoreInitializer> _logger;

    public ChatStoreInitializer(PgConversationStore conversations, PgPendingActionStore pending, PgAuditLog audit, ILogger<ChatStoreInitializer> logger)
    {
        _conversations = conversations;
        _pending = pending;
        _audit = audit;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _conversations.EnsureCreatedAsync(cancellationToken);
            await _pending.EnsureCreatedAsync(cancellationToken);
            await _audit.EnsureCreatedAsync(cancellationToken);
            _logger.LogInformation("Chathistorik, ventende handlinger og audit-log er klar i databasen.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Kunne ikke oprette tabellerne til chathistorik/audit. Kører Postgres? Start den med 'docker compose up -d'. " +
                "Chat-kald vil fejle, indtil databasen svarer.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
