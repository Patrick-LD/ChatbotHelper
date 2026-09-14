using Chatbot.Core.Security;

namespace Chatbot.Api.Endpoints;

/// <summary>
/// Hvem er nøglen? Frontend'en (fase 6) kalder <c>GET /me</c> ved login for at validere API-nøglen
/// og vise navn og roller — og for at vide, om admin-funktioner skal vises. Svaret er præcis det,
/// autentificeringen allerede har afgjort; der slås intet ekstra op.
/// </summary>
public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/me", (CurrentUser user) => Results.Ok(new MeResponse(user.UserId, user.DisplayName, user.Roles)))
            .RequireAuthorization()
            .WithName("GetMe")
            .WithTags("Auth")
            .WithSummary("Den autentificerede bruger bag X-Api-Key")
            .Produces<MeResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }
}

/// <param name="UserId">Stabilt bruger-id, f.eks. "hanne.hr". Ejer af samtaler og bruger i audit-loggen.</param>
/// <param name="Name">Visningsnavn.</param>
/// <param name="Roles">Normaliserede roller (små bogstaver), f.eks. ["medarbejder", "hr"].</param>
public sealed record MeResponse(string UserId, string Name, IReadOnlyList<string> Roles);
