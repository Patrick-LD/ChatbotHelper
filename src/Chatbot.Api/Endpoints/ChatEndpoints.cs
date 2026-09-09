using Chatbot.Core.Chat;

namespace Chatbot.Api.Endpoints;

public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/chat", async (
            ChatRequest request,
            IChatService chatService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                return Results.BadRequest(new { error = "Feltet 'message' må ikke være tomt." });
            }

            try
            {
                var result = await chatService.SendAsync(
                    new ChatTurnRequest(request.Message, request.ConversationId),
                    cancellationToken);

                return Results.Ok(new ChatResponse(
                    result.Reply,
                    result.ConversationId,
                    result.Sources.Select(s => new SourceDto(s.Source, s.Heading, Math.Round(s.Score, 3))).ToList()));
            }
            catch (HttpRequestException ex)
            {
                // Typisk fordi Ollama ikke kører — en tydelig besked sparer en fejlsøgning.
                return Results.Problem(
                    title: "Modellen kunne ikke kontaktes",
                    detail: $"Kunne ikke nå Ollama. Kører 'ollama serve', og er modellen hentet? ({ex.Message})",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .WithName("PostChat")
        .WithSummary("Send en besked til chatbotten")
        .WithDescription(
            "Returnerer modellens svar, et conversationId og de kilder i dokumentationen, botten " +
            "eventuelt slog op. Send samme conversationId med i næste kald, for at botten husker konteksten.")
        .Produces<ChatResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }
}

/// <param name="Message">Brugerens besked.</param>
/// <param name="ConversationId">Udelades i første kald — så oprettes en ny samtale.</param>
public sealed record ChatRequest(string Message, string? ConversationId);

/// <param name="Sources">Kilder botten slog op i denne tur. Tom liste, hvis den svarede uden at søge.</param>
public sealed record ChatResponse(string Reply, string ConversationId, IReadOnlyList<SourceDto> Sources);

public sealed record SourceDto(string Source, string Heading, double Score);
