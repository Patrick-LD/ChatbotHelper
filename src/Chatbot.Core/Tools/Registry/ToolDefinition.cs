using System.Text.Json;

namespace Chatbot.Core.Tools.Registry;

/// <summary>
/// Hvordan et tool udføres. Det er den ene af fase 4's to bærende idéer: et tool er data
/// (navn, beskrivelse, parametre) plus en <em>handler-type</em>, der siger, hvem der gør arbejdet.
/// Nye handler-typer er kode; nye tools af en kendt type er rækker i databasen.
/// </summary>
public enum ToolHandlerType
{
    /// <summary>En klasse i koden, der implementerer <see cref="IInternalTool"/> — f.eks. dokumentationssøgningen.</summary>
    Internal,

    /// <summary>Et HTTP-kald beskrevet i handler-konfigurationen (metode, URL, body-skabelon, svar-skabeloner).</summary>
    Http,

    /// <summary>Et tool på en MCP-server, der er registreret i <c>mcp_servers</c>.</summary>
    Mcp,
}

/// <summary>
/// Én række i tool-registret. Det er alt, modellen og runtime-loaderen behøver at vide om et tool.
/// </summary>
/// <param name="Name">Funktionsnavnet modellen ser. Små bogstaver, tal og underscore (max 64 tegn).</param>
/// <param name="Description">Teksten modellen læser, når den beslutter, om den skal kalde toolet. Skriv hvornår — og hvornår ikke.</param>
/// <param name="ParametersSchema">JSON-skema (type "object") for parametrene. Sendes uændret til modellen.</param>
/// <param name="HandlerType">Hvem der udfører kaldet.</param>
/// <param name="HandlerConfig">Handler-specifik konfiguration som JSON (HTTP: metode/URL/skabeloner, MCP: server/tool, intern: handler-nøgle).</param>
/// <param name="RequiresConfirmation">Sandt for tools der ændrer noget: kaldet fra modellen bliver til et forslag, som brugeren skal bekræfte (fase 3's to-trins-flow).</param>
/// <param name="SummaryTemplate">Menneskelæsbar opsummering af forslaget med {parameter}-pladsholdere, f.eks. "Opret {fuldeNavn} ({email}) i {afdeling}".</param>
/// <param name="IsActive">Inaktive tools ligger i tabellen, men loades aldrig — en pause-knap uden at slette.</param>
/// <param name="Roles">Roller der må bruge toolet. Et tool uden roller kan ingen bruge.</param>
public sealed record ToolDefinition(
    string Name,
    string Description,
    JsonElement ParametersSchema,
    ToolHandlerType HandlerType,
    JsonElement HandlerConfig,
    bool RequiresConfirmation,
    string? SummaryTemplate,
    bool IsActive,
    IReadOnlyList<string> Roles,
    DateTimeOffset? UpdatedAt = null);

/// <summary>En rolle, tools kan tildeles. I fase 5 kobles rollerne på rigtig autentificering.</summary>
public sealed record RoleDefinition(string Name, string Description);

/// <summary>
/// En MCP-server, registret kan hente tools fra. Stdio-servere startes som en proces
/// (<paramref name="Command"/> + <paramref name="Arguments"/>); HTTP-servere kontaktes på <paramref name="Url"/>.
/// </summary>
public sealed record McpServerDefinition(
    string Name,
    McpTransport Transport,
    string? Command,
    IReadOnlyList<string> Arguments,
    string? Url,
    bool IsActive,
    DateTimeOffset? UpdatedAt = null);

public enum McpTransport
{
    Stdio,
    Http,
}

/// <summary>
/// Tool-registret: sandheden om hvilke tools der findes, og hvem der må bruge dem.
/// Læses ved hver tur (én forespørgsel), så en ny række virker uden genstart.
/// </summary>
public interface IToolRegistry
{
    Task EnsureCreatedAsync(CancellationToken cancellationToken = default);

    /// <summary>Aktive tools, som mindst én af rollerne har adgang til — i navneorden, så modellen ser en stabil liste.</summary>
    Task<IReadOnlyList<ToolDefinition>> GetForRolesAsync(IReadOnlyCollection<string> roles, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ToolDefinition>> ListAsync(CancellationToken cancellationToken = default);

    Task<ToolDefinition?> GetAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Opretter eller erstatter et tool inkl. rolletildelinger. Ukendte roller oprettes.</summary>
    Task UpsertAsync(ToolDefinition tool, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default);

    Task<long> CountAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RoleDefinition>> ListRolesAsync(CancellationToken cancellationToken = default);

    Task UpsertRoleAsync(RoleDefinition role, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<McpServerDefinition>> ListMcpServersAsync(CancellationToken cancellationToken = default);

    Task<McpServerDefinition?> GetMcpServerAsync(string name, CancellationToken cancellationToken = default);

    Task UpsertMcpServerAsync(McpServerDefinition server, CancellationToken cancellationToken = default);

    Task<bool> DeleteMcpServerAsync(string name, CancellationToken cancellationToken = default);
}
