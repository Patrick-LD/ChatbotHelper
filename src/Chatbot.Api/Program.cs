using Chatbot.Api.Endpoints;
using Chatbot.Core.Chat;
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

var chatbotOptions = builder.Configuration
    .GetSection(ChatbotOptions.SectionName)
    .Get<ChatbotOptions>() ?? new ChatbotOptions();

// LLM-adgangen ligger bag IChatClient. Skiftet til en cloud-model (fase 3+) rører
// kun denne registrering — hverken ChatService eller endpointet ved, hvem der svarer.
builder.Services.AddChatClient(_ =>
        new OllamaApiClient(new Uri(chatbotOptions.Ollama.Endpoint), chatbotOptions.Ollama.Model))
    .UseLogging();

builder.Services.AddSingleton<IConversationStore, InMemoryConversationStore>();
builder.Services.AddScoped<IChatService, ChatService>();

var app = builder.Build();

// Står øverst i loggen ved hver opstart: peger vi på den rigtige server og model?
// Kører der flere Ollama-instanser på maskinen, er dette den hurtigste vej til at se det.
app.Logger.LogInformation(
    "Chatbot klar. Model {Model} via {Endpoint}. Historik: {MaxHistoryMessages} beskeder.",
    chatbotOptions.Ollama.Model,
    chatbotOptions.Ollama.Endpoint,
    chatbotOptions.MaxHistoryMessages);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Swagger UI på /swagger — læser dokumentet fra /openapi/v1.json.
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Chatbot API"));
}

app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).WithName("GetHealth");
app.MapChatEndpoints();

app.Run();

/// <summary>Gør Program synlig for integrationstests.</summary>
public partial class Program;
