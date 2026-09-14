using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Chatbot.Api.Security;

/// <summary>
/// Sektionen "Cors" i appsettings. Frontend'en (fase 6) hostes som statisk site på en anden origin
/// end API'et, så browseren kræver CORS i produktion. I udvikling går kald gennem Vites proxy og
/// har samme origin — listen kan derfor være tom lokalt uden at noget går i stykker.
/// </summary>
public sealed class FrontendCorsOptions
{
    public const string SectionName = "Cors";
    public const string PolicyName = "Frontend";

    /// <summary>Tilladte origins, f.eks. "https://chatbothelperweb.z6.web.core.windows.net". Tom = ingen CORS-headers.</summary>
    [ValidateEnumeratedItems]
    public List<OriginEntry> AllowedOrigins { get; set; } = [];

    /// <summary>Konfigurationsbinding lægger til et array; vi binder derfor til objekter og normaliserer her.</summary>
    public string[] Origins => AllowedOrigins
        .Select(o => o.Url.Trim().TrimEnd('/'))
        .Where(o => o.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

public sealed class OriginEntry
{
    [Required(AllowEmptyStrings = false, ErrorMessage = "Cors:AllowedOrigins[*]:Url mangler.")]
    [Url(ErrorMessage = "Cors:AllowedOrigins[*]:Url skal være en absolut URL med skema, f.eks. https://app.firma.dk.")]
    public string Url { get; set; } = string.Empty;
}

[OptionsValidator]
public sealed partial class FrontendCorsOptionsValidator : IValidateOptions<FrontendCorsOptions>;
