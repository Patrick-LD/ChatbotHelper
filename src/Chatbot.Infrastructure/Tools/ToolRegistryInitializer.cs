using Chatbot.Core.Tools;
using Chatbot.Core.Tools.Registry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chatbot.Infrastructure.Tools;

/// <summary>
/// Sørger ved opstart for, at tool-tabellerne findes, og seeder fase 3's tools, hvis registret er tomt.
/// Seed sker kun den første gang: derefter er databasen sandheden, og ændringer laves dér.
/// Fejler databasen, stopper applikationen ikke — chatten virker uden tools — men loggen siger hvorfor.
/// </summary>
public sealed class ToolRegistryInitializer : IHostedService
{
    private readonly IToolRegistry _registry;
    private readonly ToolsOptions _options;
    private readonly ILogger<ToolRegistryInitializer> _logger;

    public ToolRegistryInitializer(IToolRegistry registry, IOptions<ToolsOptions> options, ILogger<ToolRegistryInitializer> logger)
    {
        _registry = registry;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _registry.EnsureCreatedAsync(cancellationToken);

            foreach (var role in ToolSeed.Roles)
            {
                await _registry.UpsertRoleAsync(role, cancellationToken);
            }

            var count = await _registry.CountAsync(cancellationToken);
            if (count == 0)
            {
                foreach (var tool in ToolSeed.Default(_options.EmployeeApi.BaseUrl))
                {
                    await _registry.UpsertAsync(tool, cancellationToken);
                }

                count = await _registry.CountAsync(cancellationToken);
                _logger.LogInformation("Tool-registret var tomt og er seedet med {Count} tools fra fase 3.", count);
            }

            var tools = await _registry.ListAsync(cancellationToken);
            _logger.LogInformation(
                "Tool-registret er klar med {Count} tools ({Active} aktive): {Tools}",
                tools.Count,
                tools.Count(t => t.IsActive),
                string.Join(", ", tools.Select(t => t.IsActive ? t.Name : $"{t.Name} (inaktiv)")));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Kunne ikke nå tool-registret. Kører Postgres? Start den med 'docker compose up -d'. " +
                "Chatten virker, men modellen får ingen tools, indtil databasen svarer.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
