using Chatbot.Core.Security;
using Npgsql;

namespace Chatbot.Api.Endpoints;

/// <summary>Opslag i audit-loggen (fase 5.1). Kun for administratorer — loggen indeholder andre brugeres handlinger.</summary>
public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/audit", async (int? limit, string? userId, IAuditLog audit, CancellationToken ct) =>
        {
            try
            {
                var entries = await audit.ListAsync(limit ?? 50, string.IsNullOrWhiteSpace(userId) ? null : userId, ct);
                return Results.Ok(entries.Select(e => new AuditEntryDto(
                    e.Id, e.At, e.UserId, e.Roles, e.ConversationId, e.ToolName, e.Kind, e.ParametersJson, e.Result, e.Success, e.DurationMs)).ToList());
            }
            catch (NpgsqlException ex)
            {
                return Results.Problem(
                    title: "Audit-loggen kunne ikke kontaktes",
                    detail: $"Kører Postgres? ({ex.Message})",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .WithName("ListAudit")
        .WithTags("Audit")
        .WithSummary("Seneste tool-kald og handlinger med bruger, parametre og resultat")
        .WithDescription("kind: kaldt (læse-tool), forberedt (venter på ja), udført, annulleret, udløbet, afvist (ingen rettighed), fejlet.")
        .RequireAuthorization(AuthorizationPolicies.Admin)
        .Produces<List<AuditEntryDto>>();

        return app;
    }
}

public sealed record AuditEntryDto(
    long Id,
    DateTimeOffset At,
    string UserId,
    IReadOnlyList<string> Roles,
    string ConversationId,
    string ToolName,
    string Kind,
    string Parameters,
    string Result,
    bool Success,
    long DurationMs);

public static class AuthorizationPolicies
{
    /// <summary>Kræver rollen "admin". Bruges på drifts-endpoints: tool-registry, ingestion, audit.</summary>
    public const string Admin = "admin";
}
