using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Chatbot.Core.Chat;

/// <summary>
/// Konfiguration af chatten. Bindes fra sektionen "Chatbot" i appsettings.json,
/// så systemprompt og modelvalg kan justeres uden kodeændringer.
/// Valideres ved opstart — en tastefejl i konfigurationen skal stoppe applikationen
/// med en tydelig besked, ikke først vise sig som et mærkeligt svar fra botten.
/// </summary>
public sealed class ChatbotOptions
{
    public const string SectionName = "Chatbot";

    /// <summary>Systemprompten der sendes forrest i hvert kald til modellen.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Chatbot:SystemPrompt mangler — botten skal have en instruktion.")]
    public string SystemPrompt { get; set; } = "Du er en hjælpsom assistent.";

    /// <summary>
    /// Hvor mange af samtalens seneste beskeder der sendes med (systemprompten tæller ikke med).
    /// Holder prompten inden for modellens kontekstvindue i lange samtaler.
    /// 0 betyder ingen beskæring — kun brugbart i test.
    /// </summary>
    // Bemærk: source generatoren bruger rammeværkets egen (engelske) fejltekst for Range,
    // så en ErrorMessage her ville alligevel ikke blive vist.
    [Range(0, 500)]
    public int MaxHistoryMessages { get; set; } = 20;

    [Required]
    [ValidateObjectMembers]
    public OllamaOptions Ollama { get; set; } = new();
}

public sealed class OllamaOptions
{
    /// <summary>
    /// Ollamas lokale API. Skiftes ud, når der skiftes til en cloud-model.
    /// Bevidst 127.0.0.1 og ikke localhost: kører der også en Ollama i Docker, lytter den på
    /// IPv6, og "localhost" kan så ramme den forkerte server.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Chatbot:Ollama:Endpoint mangler.")]
    [Url(ErrorMessage = "Chatbot:Ollama:Endpoint skal være en URL, f.eks. http://127.0.0.1:11434.")]
    public string Endpoint { get; set; } = "http://127.0.0.1:11434";

    /// <summary>Chatmodel. Skal understøtte function calling af hensyn til fase 3.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Chatbot:Ollama:Model mangler — f.eks. llama3.1.")]
    public string Model { get; set; } = "llama3.1";
}
