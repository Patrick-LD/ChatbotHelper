using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Chatbot.Core.Tools;

/// <summary>Konfiguration af handlings-tools. Sektionen "Tools" i appsettings.json.</summary>
public sealed class ToolsOptions
{
    public const string SectionName = "Tools";

    [Required]
    [ValidateObjectMembers]
    public EmployeeApiOptions EmployeeApi { get; set; } = new();

    /// <summary>Hvor længe et forslag venter på bekræftelse, før det kasseres. Et gammelt "ja" må ikke udløse en glemt handling.</summary>
    [Range(1, 1440)]
    public int PendingActionTimeoutMinutes { get; set; } = 30;
}

public sealed class EmployeeApiOptions
{
    /// <summary>Basis-URL til HR-API'et. I fase 3 er det dummy-API'et i Chatbot.DummyHr.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Tools:EmployeeApi:BaseUrl mangler.")]
    [Url(ErrorMessage = "Tools:EmployeeApi:BaseUrl skal være en URL, f.eks. http://localhost:5100.")]
    public string BaseUrl { get; set; } = "http://localhost:5100";
}

[OptionsValidator]
public sealed partial class ToolsOptionsValidator : IValidateOptions<ToolsOptions>;
