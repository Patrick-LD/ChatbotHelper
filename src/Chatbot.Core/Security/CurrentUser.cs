namespace Chatbot.Core.Security;

/// <summary>
/// Hvem der taler med botten i dette request — afgjort af autentificeringen, ikke af en header
/// klienten selv udfylder. Fase 4's <c>X-Roles</c> var en påstand; fra fase 5.1 kommer bruger og
/// roller fra API-nøglen (eller senere en identitetsudbyder), og resten af koden kender kun denne klasse.
/// Scoped: udfyldes pr. request af web-laget.
/// </summary>
public sealed class CurrentUser
{
    public const string AdminRole = "admin";

    public static CurrentUser Anonymous { get; } = new(string.Empty, "anonym", [], isAuthenticated: false);

    public CurrentUser(string userId, string displayName, IReadOnlyList<string> roles, bool isAuthenticated = true)
    {
        UserId = userId;
        DisplayName = displayName;
        Roles = roles.Select(r => r.Trim().ToLowerInvariant()).Where(r => r.Length > 0).Distinct().ToList();
        IsAuthenticated = isAuthenticated;
    }

    /// <summary>Stabil nøgle for brugeren (f.eks. "hanne.hr"). Bruges til ejerskab af samtaler og i audit-loggen.</summary>
    public string UserId { get; }

    public string DisplayName { get; }

    /// <summary>Normaliserede roller (små bogstaver, uden dubletter). Afgør hvilke tools modellen får.</summary>
    public IReadOnlyList<string> Roles { get; }

    public bool IsAuthenticated { get; }

    public bool IsAdmin => Roles.Contains(AdminRole);
}
