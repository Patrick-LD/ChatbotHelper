using Chatbot.Api.Endpoints;
using Chatbot.Core.Chat;
using Chatbot.Core.Actions;
using Chatbot.Core.Rag;
using Chatbot.Core.Tools;
using Chatbot.Infrastructure.Hr;
using Chatbot.Infrastructure.Rag;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OllamaSharp;

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

var chatbotOptions = builder.Configuration
    .GetSection(ChatbotOptions.SectionName)
    .Get<ChatbotOptions>() ?? new ChatbotOptions();

var ragOptions = builder.Configuration
    .GetSection(RagOptions.SectionName)
    .Get<RagOptions>() ?? new RagOptions();

var toolsOptions = builder.Configuration
    .GetSection(ToolsOptions.SectionName)
    .Get<ToolsOptions>() ?? new ToolsOptions();

// LLM-adgangen ligger bag IChatClient. Skiftet til en cloud-model (fase 3+) rører
// kun denne registrering — hverken ChatService eller endpointet ved, hvem der svarer.
// UseFunctionInvocation gør pipelinen i stand til at udføre tool-kald: beder modellen om
// at kalde soeg_i_dokumentation, køres metoden, og resultatet sendes tilbage til modellen.
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

builder.Services.AddSingleton<IConversationStore, InMemoryConversationStore>();

// RAG: vektor-database, indlæsning, ingestion og søgning.
builder.Services.AddSingleton<IVectorStore, PgVectorStore>();
builder.Services.AddHostedService<VectorStoreInitializer>();
builder.Services.AddSingleton<IDocumentLoader, FileDocumentLoader>();
builder.Services.AddScoped<IngestionService>();
builder.Services.AddScoped<IDocumentSearchService, DocumentSearchService>();

// Handlings-tools (fase 3): HR-API bag IEmployeeService, ventende handlinger pr. samtale.
builder.Services.AddHttpClient<IEmployeeService, HttpEmployeeService>(client =>
{
    client.BaseAddress = new Uri(toolsOptions.EmployeeApi.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddSingleton<IPendingActionStore, InMemoryPendingActionStore>();

// Pr. request: samtalens id, hvad blev hentet i denne tur, og hvilke tools må modellen se.
// Rækkefølgen af providers er den rækkefølge, modellen ser tools i.
builder.Services.AddScoped<TurnContext>();
builder.Services.AddScoped<RetrievalContext>();
builder.Services.AddScoped<IChatToolProvider, DocumentSearchTool>();
builder.Services.AddScoped<EmployeeTools>();
builder.Services.AddScoped<IChatToolProvider>(sp => sp.GetRequiredService<EmployeeTools>());
builder.Services.AddScoped<IActionExecutor>(sp => sp.GetRequiredService<EmployeeTools>());
builder.Services.AddScoped<IChatService, ChatService>();

var app = builder.Build();

// Står øverst i loggen ved hver opstart: peger vi på den rigtige server og model?
// Kører der flere Ollama-instanser på maskinen, er dette den hurtigste vej til at se det.
app.Logger.LogInformation(
    "Chatbot klar. Model {Model} via {Endpoint}. Historik: {MaxHistoryMessages} beskeder. " +
    "Embeddings: {EmbeddingModel} ({Dimensions} dim). Dokumenter: {DocumentsPath}. HR-API: {EmployeeApi}.",
    chatbotOptions.Ollama.Model,
    chatbotOptions.Ollama.Endpoint,
    chatbotOptions.MaxHistoryMessages,
    ragOptions.Embedding.Model,
    ragOptions.Embedding.Dimensions,
    Path.GetFullPath(ragOptions.DocumentsPath),
    toolsOptions.EmployeeApi.BaseUrl);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Swagger UI på /swagger — læser dokumentet fra /openapi/v1.json.
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Chatbot API"));
}

app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).WithName("GetHealth");
app.MapChatEndpoints();
app.MapRagEndpoints();

app.Run();

/// <summary>Gør Program synlig for integrationstests.</summary>
public partial class Program;
