using System.Text.Json;
using Chatbot.Core.Tools.Registry;
using Chatbot.Infrastructure.Tools;
using Npgsql;

namespace Chatbot.Api.Endpoints;

/// <summary>
/// Administration af tool-registret (fase 4). Det er svaret på "kan en anden end dig tilføje et tool
/// uden din hjælp?": en PUT med navn, beskrivelse, skema og handler-konfiguration — så har modellen
/// toolet i næste tur, uden genstart og uden deploy.
///
/// Der er endnu ingen adgangskontrol på disse endpoints (som på /ingest). Det er fase 5.1 —
/// indtil da må API'et ikke være tilgængeligt for andre end udviklerne.
/// </summary>
public static class ToolEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapToolEndpoints(this IEndpointRouteBuilder app)
    {
        var tools = app.MapGroup("/tools").WithTags("Tool-registry");

        tools.MapGet("", async (IToolRegistry registry, CancellationToken ct) =>
            await Guard(async () => Results.Ok((await registry.ListAsync(ct)).Select(ToDto).ToList())))
            .WithName("ListTools")
            .WithSummary("Alle tools i registret — også inaktive")
            .Produces<List<ToolDto>>();

        tools.MapGet("/{name}", async (string name, IToolRegistry registry, CancellationToken ct) =>
            await Guard(async () =>
            {
                var tool = await registry.GetAsync(name, ct);
                return tool is null ? Results.NotFound(new { error = $"Toolet '{name}' findes ikke." }) : Results.Ok(ToDto(tool));
            }))
            .WithName("GetTool")
            .Produces<ToolDto>()
            .Produces(StatusCodes.Status404NotFound);

        tools.MapPut("/{name}", async (
            string name,
            ToolUpsertRequest request,
            IToolRegistry registry,
            IEnumerable<IToolHandler> handlers,
            CancellationToken ct) =>
            await Guard(async () =>
            {
                if (!Enum.TryParse<ToolHandlerType>(request.HandlerType, ignoreCase: true, out var handlerType))
                {
                    return Results.BadRequest(new { errors = new[] { "handlerType skal være internal, http eller mcp." } });
                }

                var definition = new ToolDefinition(
                    Name: name,
                    Description: request.Description ?? string.Empty,
                    ParametersSchema: request.ParametersSchema ?? Json("""{ "type": "object", "properties": {} }"""),
                    HandlerType: handlerType,
                    HandlerConfig: request.HandlerConfig ?? Json("{}"),
                    RequiresConfirmation: request.RequiresConfirmation,
                    SummaryTemplate: request.SummaryTemplate,
                    IsActive: request.IsActive,
                    Roles: (request.Roles ?? []).Select(r => r.Trim().ToLowerInvariant()).Where(r => r.Length > 0).Distinct().ToList());

                var errors = ToolDefinitionValidator.Validate(definition, handlers);
                if (errors.Count > 0)
                {
                    return Results.BadRequest(new { errors });
                }

                var existed = await registry.GetAsync(name, ct) is not null;
                await registry.UpsertAsync(definition, ct);
                var saved = (await registry.GetAsync(name, ct))!;

                return existed ? Results.Ok(ToDto(saved)) : Results.Created($"/tools/{name}", ToDto(saved));
            }))
            .WithName("PutTool")
            .WithSummary("Opret eller erstat et tool")
            .WithDescription(
                "Toolet er tilgængeligt for modellen fra næste chat-tur — ingen genstart. handlerType afgør, hvad handlerConfig skal " +
                "indeholde: http → method, url, evt. body/headers/skabeloner; mcp → server, tool; internal → handler (nøgle i koden). " +
                "Et tool uden roller kan ingen bruge.")
            .Produces<ToolDto>()
            .Produces<ToolDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest);

        tools.MapDelete("/{name}", async (string name, IToolRegistry registry, CancellationToken ct) =>
            await Guard(async () => await registry.DeleteAsync(name, ct)
                ? Results.NoContent()
                : Results.NotFound(new { error = $"Toolet '{name}' findes ikke." })))
            .WithName("DeleteTool")
            .WithSummary("Slet et tool (sæt hellere isActive=false, hvis det kan blive relevant igen)")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        var roles = app.MapGroup("/roles").WithTags("Tool-registry");

        roles.MapGet("", async (IToolRegistry registry, CancellationToken ct) =>
            await Guard(async () => Results.Ok(await registry.ListRolesAsync(ct))))
            .WithName("ListRoles")
            .Produces<List<RoleDefinition>>();

        roles.MapPut("/{name}", async (string name, RoleUpsertRequest request, IToolRegistry registry, CancellationToken ct) =>
            await Guard(async () =>
            {
                var normalized = name.Trim().ToLowerInvariant();
                if (!ToolDefinitionValidator.RoleNamePattern().IsMatch(normalized))
                {
                    return Results.BadRequest(new { errors = new[] { "Rollenavne består af små bogstaver, tal, bindestreg og underscore." } });
                }

                await registry.UpsertRoleAsync(new RoleDefinition(normalized, request.Description ?? string.Empty), ct);
                return Results.Ok(new RoleDefinition(normalized, request.Description ?? string.Empty));
            }))
            .WithName("PutRole")
            .Produces<RoleDefinition>()
            .Produces(StatusCodes.Status400BadRequest);

        var servers = app.MapGroup("/mcp-servers").WithTags("MCP");

        servers.MapGet("", async (IToolRegistry registry, CancellationToken ct) =>
            await Guard(async () => Results.Ok((await registry.ListMcpServersAsync(ct)).Select(ToDto).ToList())))
            .WithName("ListMcpServers")
            .Produces<List<McpServerDto>>();

        servers.MapPut("/{name}", async (string name, McpServerUpsertRequest request, IToolRegistry registry, McpClientPool pool, CancellationToken ct) =>
            await Guard(async () =>
            {
                if (!Enum.TryParse<McpTransport>(request.Transport, ignoreCase: true, out var transport))
                {
                    return Results.BadRequest(new { errors = new[] { "transport skal være stdio eller http." } });
                }

                var errors = new List<string>();
                if (!ToolDefinitionValidator.RoleNamePattern().IsMatch(name)) errors.Add("Servernavnet består af små bogstaver, tal, bindestreg og underscore.");
                if (transport == McpTransport.Stdio && string.IsNullOrWhiteSpace(request.Command)) errors.Add("stdio-servere skal have en command (f.eks. npx).");
                if (transport == McpTransport.Http && (!Uri.TryCreate(request.Url, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))) errors.Add("http-servere skal have en absolut url.");
                if (errors.Count > 0)
                {
                    return Results.BadRequest(new { errors });
                }

                var definition = new McpServerDefinition(name, transport, request.Command, request.Arguments ?? [], request.Url, request.IsActive);
                await registry.UpsertMcpServerAsync(definition, ct);
                pool.Invalidate(name);
                return Results.Ok(ToDto((await registry.GetMcpServerAsync(name, ct))!));
            }))
            .WithName("PutMcpServer")
            .WithSummary("Registrér en MCP-server (stdio: command + arguments, http: url)")
            .Produces<McpServerDto>()
            .Produces(StatusCodes.Status400BadRequest);

        servers.MapDelete("/{name}", async (string name, IToolRegistry registry, McpClientPool pool, CancellationToken ct) =>
            await Guard(async () =>
            {
                pool.Invalidate(name);
                return await registry.DeleteMcpServerAsync(name, ct)
                    ? Results.NoContent()
                    : Results.NotFound(new { error = $"MCP-serveren '{name}' findes ikke." });
            }))
            .WithName("DeleteMcpServer")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        servers.MapGet("/{name}/tools", async (string name, McpClientPool pool, CancellationToken ct) =>
            await Guard(async () =>
            {
                var mcpTools = await pool.ListToolsAsync(name, ct);
                return Results.Ok(mcpTools.Select(t => new McpToolDto(t.Name, t.Description, t.JsonSchema)).ToList());
            }))
            .WithName("ListMcpServerTools")
            .WithSummary("Toolene som MCP-serveren selv tilbyder (starter serveren, hvis den ikke kører)")
            .Produces<List<McpToolDto>>()
            .ProducesProblem(StatusCodes.Status502BadGateway);

        servers.MapPost("/{name}/import", async (string name, McpImportRequest request, IToolRegistry registry, McpClientPool pool, CancellationToken ct) =>
            await Guard(async () =>
            {
                var wanted = request.Tools is { Length: > 0 } ? new HashSet<string>(request.Tools, StringComparer.OrdinalIgnoreCase) : null;
                var roleList = (request.Roles ?? []).Select(r => r.Trim().ToLowerInvariant()).Where(r => r.Length > 0).Distinct().ToList();
                var prefix = string.IsNullOrWhiteSpace(request.Prefix) ? name : request.Prefix;

                var imported = new List<string>();
                var skipped = new List<string>();
                foreach (var mcpTool in await pool.ListToolsAsync(name, ct))
                {
                    if (wanted is not null && !wanted.Contains(mcpTool.Name))
                    {
                        skipped.Add(mcpTool.Name);
                        continue;
                    }

                    var toolName = SanitizeName($"{prefix}_{mcpTool.Name}");
                    var definition = new ToolDefinition(
                        Name: toolName,
                        Description: string.IsNullOrWhiteSpace(mcpTool.Description)
                            ? $"Toolet '{mcpTool.Name}' på MCP-serveren '{name}'."
                            : mcpTool.Description,
                        ParametersSchema: mcpTool.JsonSchema,
                        HandlerType: ToolHandlerType.Mcp,
                        HandlerConfig: Json(JsonSerializer.Serialize(new { server = name, tool = mcpTool.Name }, JsonOptions)),
                        RequiresConfirmation: request.RequiresConfirmation,
                        SummaryTemplate: request.RequiresConfirmation ? $"Kør {mcpTool.Name} på {name}" : null,
                        IsActive: request.IsActive,
                        Roles: roleList);

                    await registry.UpsertAsync(definition, ct);
                    imported.Add(toolName);
                }

                return Results.Ok(new McpImportResponse(imported, skipped, roleList));
            }))
            .WithName("ImportMcpServerTools")
            .WithSummary("Importér serverens tools som rækker i registret")
            .WithDescription(
                "Hvert MCP-tool bliver en tool-række med navnet <prefix>_<tool>, serverens egen beskrivelse og JSON-skema, " +
                "handlerType=mcp og de angivne roller. Uden roller kan ingen bruge dem, før de tildeles via PUT /tools/{name}. " +
                "Kør igen for at opdatere efter en serveropgradering.")
            .Produces<McpImportResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway);

        return app;
    }

    private static async Task<IResult> Guard(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (NpgsqlException ex)
        {
            return Results.Problem(
                title: "Tool-registret kunne ikke kontaktes",
                detail: $"Kører Postgres? Start den med 'docker compose up -d'. ({ex.Message})",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(title: "Ugyldig forespørgsel", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Typisk en MCP-server der ikke kunne startes (npx mangler, forkert kommando) eller ikke svarer.
            return Results.Problem(title: "MCP-serveren kunne ikke bruges", detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>MCP-toolnavne kan indeholde bindestreger og store bogstaver; registret kræver [a-z0-9_].</summary>
    internal static string SanitizeName(string raw)
    {
        var chars = raw.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray();
        var name = new string(chars).Trim('_');
        if (name.Length == 0 || !char.IsAsciiLetter(name[0]))
        {
            name = "t_" + name;
        }

        return name.Length <= 64 ? name : name[..64].TrimEnd('_');
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static ToolDto ToDto(ToolDefinition t) => new(
        t.Name, t.Description, t.ParametersSchema, t.HandlerType.ToString().ToLowerInvariant(), t.HandlerConfig,
        t.RequiresConfirmation, t.SummaryTemplate, t.IsActive, t.Roles, t.UpdatedAt);

    private static McpServerDto ToDto(McpServerDefinition s) => new(
        s.Name, s.Transport.ToString().ToLowerInvariant(), s.Command, s.Arguments, s.Url, s.IsActive, s.UpdatedAt);
}

public sealed record ToolDto(
    string Name,
    string Description,
    JsonElement ParametersSchema,
    string HandlerType,
    JsonElement HandlerConfig,
    bool RequiresConfirmation,
    string? SummaryTemplate,
    bool IsActive,
    IReadOnlyList<string> Roles,
    DateTimeOffset? UpdatedAt);

/// <param name="Description">Teksten modellen vælger toolet ud fra. Skriv hvornår det skal bruges — og hvornår ikke.</param>
/// <param name="ParametersSchema">JSON-skema med "type": "object". Udelades → ingen parametre.</param>
/// <param name="HandlerType">internal, http eller mcp.</param>
/// <param name="HandlerConfig">Afhænger af handlerType — se PUT-beskrivelsen.</param>
/// <param name="RequiresConfirmation">Sandt for tools der ændrer noget. Kræver en summaryTemplate.</param>
/// <param name="SummaryTemplate">"Opret {fuldeNavn} i {afdeling}" — det brugeren siger ja til.</param>
/// <param name="Roles">Roller der må bruge toolet. Ukendte roller oprettes.</param>
public sealed record ToolUpsertRequest(
    string? Description,
    JsonElement? ParametersSchema,
    string HandlerType,
    JsonElement? HandlerConfig,
    bool RequiresConfirmation = false,
    string? SummaryTemplate = null,
    bool IsActive = true,
    string[]? Roles = null);

public sealed record RoleUpsertRequest(string? Description);

public sealed record McpServerDto(string Name, string Transport, string? Command, IReadOnlyList<string> Arguments, string? Url, bool IsActive, DateTimeOffset? UpdatedAt);

/// <param name="Transport">stdio (lokal proces) eller http (Streamable HTTP-endpoint).</param>
/// <param name="Command">stdio: programmet, f.eks. "npx".</param>
/// <param name="Arguments">stdio: argumenter, f.eks. ["-y", "@modelcontextprotocol/server-filesystem", "C:\\data"].</param>
/// <param name="Url">http: serverens endpoint.</param>
public sealed record McpServerUpsertRequest(string Transport, string? Command = null, string[]? Arguments = null, string? Url = null, bool IsActive = true);

public sealed record McpToolDto(string Name, string? Description, JsonElement InputSchema);

/// <param name="Roles">Roller de importerede tools tildeles. Tom = ingen kan bruge dem endnu.</param>
/// <param name="Tools">Kun disse tools (serverens navne). Tom = alle.</param>
/// <param name="Prefix">Præfiks til tool-navnene. Standard = servernavnet.</param>
/// <param name="RequiresConfirmation">Sæt sandt for servere, hvis tools ændrer noget (skriver filer, sender mails).</param>
public sealed record McpImportRequest(string[]? Roles = null, string[]? Tools = null, string? Prefix = null, bool RequiresConfirmation = false, bool IsActive = true);

public sealed record McpImportResponse(IReadOnlyList<string> Imported, IReadOnlyList<string> Skipped, IReadOnlyList<string> Roles);
