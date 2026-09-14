using Chatbot.Core.Chat;
using Npgsql;

namespace Chatbot.Api.Endpoints;

public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        // Brugeren og rollerne kommer fra autentificeringen (X-Api-Key → CurrentUser), ikke fra requestet.
        // Fallback-policyen kræver en autentificeret bruger på alle endpoints — RequireAuthorization her er
        // for læsbarhedens skyld.
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
                    result.Sources.Select(s => new SourceDto(s.Source, s.Heading, Math.Round(s.Score, 3))).ToList(),
                    result.PendingAction));
            }
            catch (ConversationOwnershipException)
            {
                // Ikke "findes ikke": brugeren skal vide, at id'et er gyldigt men ikke deres — og loggen har hvem der prøvede.
                return Results.Problem(
                    title: "Samtalen tilhører en anden bruger",
                    detail: "Start en ny samtale ved at udelade conversationId.",
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Ollama svarede ikke inden Chatbot:Ollama:TimeoutSeconds — typisk fordi modellen er
                // langsom, eller fordi flere kald står i kø. Ikke en fejl i koden, og ikke en 500.
                return Results.Problem(
                    title: "Modellen svarede ikke i tide",
                    detail: "Ollama nåede ikke at svare inden timeouten (Chatbot:Ollama:TimeoutSeconds). Prøv igen, eller hæv grænsen.",
                    statusCode: StatusCodes.Status504GatewayTimeout);
            }
            catch (HttpRequestException ex)
            {
                // Typisk fordi Ollama ikke kører — en tydelig besked sparer en fejlsøgning.
                return Results.Problem(
                    title: "Modellen kunne ikke kontaktes",
                    detail: $"Kunne ikke nå Ollama. Kører 'ollama serve', og er modellen hentet? ({ex.Message})",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (NpgsqlException ex)
            {
                // Chathistorik og ventende handlinger ligger i databasen (fase 5.2) — uden den kan turen ikke gemmes.
                return Results.Problem(
                    title: "Databasen kunne ikke kontaktes",
                    detail: $"Kører Postgres? Start den med 'docker compose up -d'. ({ex.Message})",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .RequireAuthorization()
        .WithName("PostChat")
        .WithSummary("Send en besked til chatbotten")
        .WithDescription(
            "Kræver X-Api-Key. Returnerer modellens svar, et conversationId, de kilder i dokumentationen botten eventuelt slog op, " +
            "og en eventuel handling der venter på bekræftelse. Send samme conversationId med i næste kald — kun den bruger, " +
            "der startede samtalen, kan fortsætte den.")
        .Produces<ChatResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        return app;
    }
}

/// <param name="Message">Brugerens besked.</param>
/// <param name="ConversationId">Udelades i første kald — så oprettes en ny samtale.</param>
public sealed record ChatRequest(string Message, string? ConversationId);

/// <param name="Sources">Kilder botten slog op i denne tur. Tom liste, hvis den svarede uden at søge.</param>
/// <param name="PendingAction">Handling der venter på brugerens bekræftelse ("ja"/"nej" i næste besked). Null hvis ingen.</param>
public sealed record ChatResponse(string Reply, string ConversationId, IReadOnlyList<SourceDto> Sources, string? PendingAction);

public sealed record SourceDto(string Source, string Heading, double Score);
