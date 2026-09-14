using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Chatbot.Core.Tools;

/// <summary>Konfiguration af handlings-tools og tool-registret. Sektionen "Tools" i appsettings.json.</summary>
public sealed class ToolsOptions
{
    public const string SectionName = "Tools";

    /// <summary>HR-API'et. Fra fase 4 bruges adressen kun til at seede registret første gang — derefter står URL'en i tool-rækken.</summary>
    [Required]
    [ValidateObjectMembers]
    public EmployeeApiOptions EmployeeApi { get; set; } = new();

    [Required]
    [ValidateObjectMembers]
    public ToolRegistryOptions Registry { get; set; } = new();

    /// <summary>Hvor længe et forslag venter på bekræftelse, før det kasseres. Et gammelt "ja" må ikke udløse en glemt handling.</summary>
    [Range(1, 1440)]
    public int PendingActionTimeoutMinutes { get; set; } = 30;

    /// <summary>
    /// Roller en tur får, når kaldet ikke selv angiver nogen (X-Roles-headeren). Indtil fase 5.1 kobler
    /// rigtig autentificering på, er det her, "hvem er brugeren" afgøres — sæt listen tom for at kræve headeren.
    /// </summary>
    public string[] DefaultRoles { get; set; } = ["medarbejder", "hr"];
}

public sealed class EmployeeApiOptions
{
    /// <summary>Basis-URL til HR-API'et. I fase 3/4 er det dummy-API'et i Chatbot.DummyHr.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Tools:EmployeeApi:BaseUrl mangler.")]
    [Url(ErrorMessage = "Tools:EmployeeApi:BaseUrl skal være en URL, f.eks. http://localhost:5100.")]
    public string BaseUrl { get; set; } = "http://localhost:5100";
}

public sealed class ToolRegistryOptions
{
    /// <summary>Postgres med tool-tabellerne. Som udgangspunkt samme database som vektor-indekset — én database til det hele.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Tools:Registry:ConnectionString mangler.")]
    public string ConnectionString { get; set; } = "Host=127.0.0.1;Port=5432;Database=chatbot;Username=chatbot;Password=chatbot";

    /// <summary>Hvor længe et MCP-server-kald (opstart + tool-kald) må tage.</summary>
    [Range(5, 600)]
    public int McpTimeoutSeconds { get; set; } = 60;

    /// <summary>Længste tool-svar der gives videre til modellen. Længere svar klippes — et kontekstvindue er ikke uendeligt.</summary>
    [Range(500, 100_000)]
    public int MaxResultChars { get; set; } = 8000;
}

[OptionsValidator]
public sealed partial class ToolsOptionsValidator : IValidateOptions<ToolsOptions>;
