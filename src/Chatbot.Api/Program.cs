using Chatbot.Api.Endpoints;
using Chatbot.Api.Security;
using Chatbot.Core.Chat;
using Chatbot.Core.Actions;
using Chatbot.Core.Rag;
using Chatbot.Core.Security;
using Chatbot.Core.Tools;
using Chatbot.Core.Tools.Registry;
using Chatbot.Infrastructure.Chat;
using Chatbot.Infrastructure.Rag;
using Chatbot.Infrastructure.Security;
using Chatbot.Infrastructure.Tools;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using OllamaSharp;
using Polly;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// Konfigurationen valideres ved opstart: en tom systemprompt eller et ugyldigt endpoint
// stopper applikationen med en tydelig besked i stedet for at fejle ved første chat-kald.
builder.Services.AddSingleton<IValidateOptions<ChatbotOptions>, ChatbotOptionsValidator>();
builder.Services
    .AddOptions<ChatbotOptions>()
    .Bind(builder.Configuration.GetSection(ChatbotOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddSingleton<IValidateOptions<RagOptions>, RagOptionsValidator>();
builder.Services
    .AddOptions<RagOptions>()
    .Bind(builder.Configuration.GetSection(RagOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddSingleton<IValidateOptions<ToolsOptions>, ToolsOptionsValidator>();
builder.Services
    .AddOptions<ToolsOptions>()
    .Bind(builder.Configuration.GetSection(ToolsOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddSingleton<IValidateOptions<AuthOptions>, AuthOptionsValidator>();
builder.Services
    .AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection(AuthOptions.SectionName))
    .ValidateOnStart();

var chatbotOptions = builder.Configuration
    .GetSection(ChatbotOptions.SectionName)
    .Get<ChatbotOptions>() ?? new ChatbotOptions();

var ragOptions = builder.Configuration
    .GetSection(RagOptions.SectionName)
    .Get<RagOptions>() ?? new RagOptions();

var toolsOptions = builder.Configuration
    .GetSection(ToolsOptions.SectionName)
    .Get<ToolsOptions>() ?? new ToolsOptions();

var authOptions = builder.Configuration
    .GetSection(AuthOptions.SectionName)
    .Get<AuthOptions>() ?? new AuthOptions();

// Autentificering (fase 5.1): API-nøgle i X-Api-Key → bruger-id og roller som claims. Alle endpoints
// undtagen /health kræver en bruger; drifts-endpoints kræver rollen admin. En identitetsudbyder
// (Entra ID/JWT) tilføjes senere som endnu en handler — CurrentUser bygges af claims uanset kilde.
builder.Services
    .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(AuthorizationPolicies.Admin, policy => policy.RequireRole(CurrentUser.AdminRole));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>(sp =>
    ApiKeyAuthenticationHandler.ToCurrentUser(sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.User));

// LLM-adgangen ligger bag IChatClient. Skiftet til en cloud-model rører kun denne registrering —
// hverken ChatService eller endpointet ved, hvem der svarer. UseFunctionInvocation udfører tool-kald.
// Egen HttpClient til Ollama, så timeouten kan sættes: en lokal model med tool-kald kan tage over
// 100 s (OllamaSharps standard), især når flere kald står i kø.
HttpClient CreateOllamaHttpClient() => new()
{
    BaseAddress = new Uri(chatbotOptions.Ollama.Endpoint),
    Timeout = TimeSpan.FromSeconds(chatbotOptions.Ollama.TimeoutSeconds),
};

builder.Services.AddChatClient(_ =>
        new OllamaApiClient(CreateOllamaHttpClient(), chatbotOptions.Ollama.Model))
    .UseFunctionInvocation()
    .UseLogging();

// Embeddings går samme vej: én registrering, resten af koden kender kun IEmbeddingGenerator.
builder.Services.AddEmbeddingGenerator(_ =>
        new OllamaApiClient(CreateOllamaHttpClient(), ragOptions.Embedding.Model))
    .UseLogging();

// RAG: vektor-database, indlæsning, ingestion og søgning.
builder.Services.AddSingleton<IVectorStore, PgVectorStore>();
builder.Services.AddHostedService<VectorStoreInitializer>();
builder.Services.AddSingleton<IDocumentLoader, FileDocumentLoader>();
builder.Services.AddScoped<IngestionService>();
builder.Services.AddScoped<IDocumentSearchService, DocumentSearchService>();

// Chathistorik, ventende handlinger og audit-log i Postgres (fase 5), så samtaler overlever genstart,
// og hvert tool-kald kan slås op bagefter.
builder.Services.AddSingleton<PgConversationStore>();
builder.Services.AddSingleton<IConversationStore>(sp => sp.GetRequiredService<PgConversationStore>());
builder.Services.AddSingleton<PgPendingActionStore>();
builder.Services.AddSingleton<IPendingActionStore>(sp => sp.GetRequiredService<PgPendingActionStore>());
builder.Services.AddSingleton<PgAuditLog>();
builder.Services.AddSingleton<IAuditLog>(sp => sp.GetRequiredService<PgAuditLog>());
builder.Services.AddHostedService<ChatStoreInitializer>();

// Tool-registret (fase 4): tools er rækker i Postgres, ikke klasser i koden. Registret læses
// pr. tur og filtreres på brugerens roller; seedes første gang med fase 3's tre tools.
builder.Services.AddSingleton<IToolRegistry, PgToolRegistry>();
builder.Services.AddHostedService<ToolRegistryInitializer>();

// Én handler pr. tool-type. Nye tool-TYPER er kode; nye tools af en kendt type er data.
// HTTP-kald til eksterne systemer får retries (fase 5.2) — men KUN for idempotente metoder:
// et POST, der nåede frem men timede ud, må ikke sendes igen og oprette to gange.
builder.Services.AddHttpClient(HttpToolHandler.HttpClientName)
    .AddResilienceHandler("tools", pipeline =>
    {
        pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = toolsOptions.Registry.HttpRetries,
            Delay = TimeSpan.FromMilliseconds(300),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = args =>
            {
                var method = args.Outcome.Result?.RequestMessage?.Method;
                var idempotent = method is null || method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Options;
                if (!idempotent)
                {
                    return ValueTask.FromResult(false);
                }

                return HttpClientResiliencePredicates.IsTransient(args.Outcome)
                    ? ValueTask.FromResult(true)
                    : ValueTask.FromResult(false);
            },
        });
    });
builder.Services.AddSingleton<IToolHandler, HttpToolHandler>();
builder.Services.AddSingleton<McpClientPool>();
builder.Services.AddSingleton<IToolHandler, McpToolHandler>();
builder.Services.AddScoped<IToolHandler, InternalToolHandler>();

// Interne tools: kode, registret kan pege på. Dokumentationssøgningen er scoped, fordi den
// registrerer hits i turens RetrievalContext.
builder.Services.AddScoped<IInternalTool, DocumentSearchTool>();

// Pr. request: samtalens id og bruger, hvad blev hentet i denne tur, og hvilke tools må modellen se.
builder.Services.AddScoped<TurnContext>();
builder.Services.AddScoped<RetrievalContext>();
builder.Services.AddScoped<IChatToolProvider, DynamicToolProvider>();
builder.Services.AddScoped<IActionExecutor, DynamicActionExecutor>();
builder.Services.AddScoped<IChatService, ChatService>();

var app = builder.Build();

// Står øverst i loggen ved hver opstart: peger vi på den rigtige server og model?
app.Logger.LogInformation(
    "Chatbot klar. Model {Model} via {Endpoint}. Historik: {MaxHistoryMessages} beskeder. " +
    "Embeddings: {EmbeddingModel} ({Dimensions} dim). Dokumenter: {DocumentsPath}. " +
    "API-nøgler: {ApiKeyCount} ({Admins} med admin). HR-API (seed): {EmployeeApi}.",
    chatbotOptions.Ollama.Model,
    chatbotOptions.Ollama.Endpoint,
    chatbotOptions.MaxHistoryMessages,
    ragOptions.Embedding.Model,
    ragOptions.Embedding.Dimensions,
    Path.GetFullPath(ragOptions.DocumentsPath),
    authOptions.ApiKeys.Count,
    authOptions.ApiKeys.Count(k => k.Roles.Contains(CurrentUser.AdminRole, StringComparer.OrdinalIgnoreCase)),
    toolsOptions.EmployeeApi.BaseUrl);

if (authOptions.ApiKeys.Count == 0)
{
    app.Logger.LogWarning(
        "Ingen API-nøgler i Auth:ApiKeys — ingen kan bruge chatten. Tilføj nøgler i user-secrets eller miljøvariabler " +
        "(Auth__ApiKeys__0__Key osv.). I Development ligger eksempelnøgler i appsettings.Development.json.");
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    // Swagger UI på /swagger — læser dokumentet fra /openapi/v1.json.
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Chatbot API"));
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).WithName("GetHealth").AllowAnonymous();
app.MapChatEndpoints();
app.MapRagEndpoints();
app.MapToolEndpoints();
app.MapAuditEndpoints();

app.Run();

/// <summary>Gør Program synlig for integrationstests.</summary>
public partial class Program;
