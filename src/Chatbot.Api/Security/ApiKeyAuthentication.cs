using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Chatbot.Core.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Chatbot.Api.Security;

/// <summary>
/// Fase 5.1: hvem er brugeren? Svaret kommer fra en API-nøgle i headeren <c>X-Api-Key</c>, der er
/// koblet til et bruger-id og roller i konfigurationen (<c>Auth:ApiKeys</c>). Nøglen erstatter fase 4's
/// <c>X-Roles</c>-header, som klienten selv kunne udfylde.
///
/// Det er bevidst ASP.NET's almindelige autentificerings-pipeline (<see cref="AuthenticationHandler{T}"/>
/// og claims), så skiftet til en identitetsudbyder (Entra ID/JWT) er én ny handler ved siden af — resten
/// af koden læser kun <see cref="CurrentUser"/>, som bygges af claims.
/// </summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    private readonly AuthOptions _auth;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        IOptions<AuthOptions> auth,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
        => _auth = auth.Value;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var header) || string.IsNullOrWhiteSpace(header))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var presented = header.ToString().Trim();
        var match = _auth.ApiKeys.FirstOrDefault(k => FixedTimeEquals(k.Key, presented));
        if (match is null)
        {
            Logger.LogWarning("Afvist API-nøgle fra {RemoteIp}.", Context.Connection.RemoteIpAddress);
            return Task.FromResult(AuthenticateResult.Fail("Ukendt API-nøgle."));
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, match.UserId),
            new(ClaimTypes.Name, string.IsNullOrWhiteSpace(match.Name) ? match.UserId : match.Name),
        };
        claims.AddRange(match.Roles.Select(r => new Claim(ClaimTypes.Role, r.Trim().ToLowerInvariant())));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "application/json; charset=utf-8";
        return Response.WriteAsync("""{"error":"Autentificering kræves. Send API-nøglen i headeren X-Api-Key."}""");
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        Response.ContentType = "application/json; charset=utf-8";
        return Response.WriteAsync("""{"error":"Din rolle giver ikke adgang til dette endpoint (kræver admin)."}""");
    }

    /// <summary>Sammenligning i konstant tid, så svartiden ikke afslører, hvor mange tegn der matchede.</summary>
    private static bool FixedTimeEquals(string expected, string actual)
    {
        var a = System.Text.Encoding.UTF8.GetBytes(expected);
        var b = System.Text.Encoding.UTF8.GetBytes(actual);
        return a.Length == b.Length && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>Bygger den scopede <see cref="CurrentUser"/> af requestets claims — uanset hvilken handler der satte dem.</summary>
    public static CurrentUser ToCurrentUser(ClaimsPrincipal? principal)
    {
        if (principal?.Identity is not { IsAuthenticated: true })
        {
            return CurrentUser.Anonymous;
        }

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.Identity.Name ?? "ukendt";
        var name = principal.Identity.Name ?? userId;
        var roles = principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        return new CurrentUser(userId, name, roles);
    }
}

/// <summary>Sektionen "Auth" i appsettings. Nøglerne hører hjemme i user-secrets eller miljøvariabler, ikke i git.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    [ValidateEnumeratedItems]
    public List<ApiKeyEntry> ApiKeys { get; set; } = [];
}

public sealed class ApiKeyEntry
{
    /// <summary>Selve hemmeligheden. Mindst 16 tegn — en kort nøgle kan gættes.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Auth:ApiKeys[*]:Key mangler.")]
    [MinLength(16, ErrorMessage = "Auth:ApiKeys[*]:Key skal være mindst 16 tegn.")]
    public string Key { get; set; } = string.Empty;

    /// <summary>Stabilt bruger-id, f.eks. "hanne.hr". Bruges til ejerskab af samtaler og i audit-loggen.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Auth:ApiKeys[*]:UserId mangler.")]
    public string UserId { get; set; } = string.Empty;

    public string? Name { get; set; }

    /// <summary>Roller i tool-registret (f.eks. medarbejder, hr) og evt. "admin" til drifts-endpoints.</summary>
    public string[] Roles { get; set; } = [];
}

[OptionsValidator]
public sealed partial class AuthOptionsValidator : IValidateOptions<AuthOptions>;
