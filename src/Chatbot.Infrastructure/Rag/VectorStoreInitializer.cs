using Chatbot.Core.Rag;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Chatbot.Infrastructure.Rag;

/// <summary>
/// Sørger ved opstart for, at tabellen findes, og logger hvor mange chunks indekset har.
/// Fejler databasen, stopper applikationen ikke — chatten virker stadig, blot uden
/// dokumentationssøgning — men fejlen står tydeligt i loggen sammen med, hvad man skal gøre.
/// </summary>
public sealed class VectorStoreInitializer : IHostedService
{
    private readonly IVectorStore _store;
    private readonly ILogger<VectorStoreInitializer> _logger;

    public VectorStoreInitializer(IVectorStore store, ILogger<VectorStoreInitializer> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _store.EnsureCreatedAsync(cancellationToken);
            var count = await _store.CountAsync(cancellationToken);

            if (count == 0)
            {
                _logger.LogWarning(
                    "Vektor-databasen er klar, men indekset er tomt. Kør POST /ingest for at indeksere dokumentationen.");
            }
            else
            {
                _logger.LogInformation("Vektor-databasen er klar med {Count} chunks.", count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Kunne ikke nå vektor-databasen. Kører Postgres? Start den med 'docker compose up -d'. " +
                "Chatten virker, men dokumentationssøgning fejler, indtil databasen svarer.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
