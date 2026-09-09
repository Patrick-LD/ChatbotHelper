using Chatbot.Core.Chat;
using Chatbot.Core.Rag;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Chatbot.Tests;

public class ChatServiceTests
{
    private const string SystemPrompt = "Du er en testassistent.";

    private static ChatService CreateService(
        FakeChatClient client,
        int maxHistoryMessages = 20,
        IEnumerable<IChatToolProvider>? tools = null,
        RetrievalContext? retrieved = null)
        => new(
            client,
            new InMemoryConversationStore(),
            tools ?? [],
            retrieved ?? new RetrievalContext(),
            Options.Create(new ChatbotOptions
            {
                SystemPrompt = SystemPrompt,
                MaxHistoryMessages = maxHistoryMessages,
            }),
            NullLogger<ChatService>.Instance);

    [Fact]
    public async Task SendAsync_sender_systemprompt_foerst()
    {
        var client = new FakeChatClient();
        var service = CreateService(client);

        await service.SendAsync(new ChatTurnRequest("Hej"));

        Assert.Equal(ChatRole.System, client.LastPrompt[0].Role);
        Assert.Equal(SystemPrompt, client.LastPrompt[0].Text);
        Assert.Equal("Hej", client.LastPrompt[^1].Text);
    }

    [Fact]
    public async Task SendAsync_uden_conversationId_starter_ny_samtale()
    {
        var service = CreateService(new FakeChatClient());

        var first = await service.SendAsync(new ChatTurnRequest("Hej"));
        var second = await service.SendAsync(new ChatTurnRequest("Hej igen"));

        Assert.NotEqual(first.ConversationId, second.ConversationId);
    }

    [Fact]
    public async Task SendAsync_sender_tidligere_beskeder_med_i_samme_samtale()
    {
        var client = new FakeChatClient("Hej Rene.");
        var service = CreateService(client);

        var first = await service.SendAsync(new ChatTurnRequest("Jeg hedder Rene"));
        await service.SendAsync(new ChatTurnRequest("Hvad hedder jeg?", first.ConversationId));

        // Systemprompt + tur 1 (bruger + assistent) + tur 2's brugerbesked.
        Assert.Equal(4, client.LastPrompt.Count);
        Assert.Equal("Jeg hedder Rene", client.LastPrompt[1].Text);
        Assert.Equal("Hej Rene.", client.LastPrompt[2].Text);
        Assert.Equal("Hvad hedder jeg?", client.LastPrompt[3].Text);
    }

    [Fact]
    public async Task SendAsync_beskaerer_historikken_til_MaxHistoryMessages()
    {
        var client = new FakeChatClient();
        var service = CreateService(client, maxHistoryMessages: 2);

        var conversationId = (await service.SendAsync(new ChatTurnRequest("Besked 1"))).ConversationId;
        await service.SendAsync(new ChatTurnRequest("Besked 2", conversationId));
        await service.SendAsync(new ChatTurnRequest("Besked 3", conversationId));

        // Systemprompt + de 2 seneste historikbeskeder + den nye brugerbesked.
        Assert.Equal(4, client.LastPrompt.Count);
        Assert.Equal("Besked 3", client.LastPrompt[^1].Text);
        Assert.DoesNotContain(client.LastPrompt, m => m.Text == "Besked 1");
    }

    [Fact]
    public async Task SendAsync_afviser_tom_besked()
    {
        var client = new FakeChatClient();
        var service = CreateService(client);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SendAsync(new ChatTurnRequest("   ")));
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task SendAsync_giver_modellen_tools_fra_alle_providers()
    {
        var client = new FakeChatClient();
        var service = CreateService(client, tools:
        [
            new StubToolProvider("tool_a"),
            new StubToolProvider("tool_b"),
        ]);

        await service.SendAsync(new ChatTurnRequest("Hej"));

        var tools = client.LastOptions?.Tools;
        Assert.NotNull(tools);
        Assert.Equal(["tool_a", "tool_b"], tools.Select(t => t.Name).ToArray());
    }

    [Fact]
    public async Task SendAsync_uden_tools_sender_ingen_ChatOptions()
    {
        var client = new FakeChatClient();
        var service = CreateService(client);

        await service.SendAsync(new ChatTurnRequest("Hej"));

        Assert.Null(client.LastOptions);
    }

    [Fact]
    public async Task SendAsync_returnerer_kilder_fra_hentede_chunks_uden_dubletter()
    {
        var retrieved = new RetrievalContext();
        retrieved.Add(
        [
            new SearchHit(Chunk("haandbog.md", "Ferie", 0), 0.81),
            new SearchHit(Chunk("haandbog.md", "Ferie", 1), 0.75),
            new SearchHit(Chunk("it.md", "Adgange", 0), 0.62),
        ]);
        var service = CreateService(new FakeChatClient(), retrieved: retrieved);

        var result = await service.SendAsync(new ChatTurnRequest("Hvor mange feriedage har jeg?"));

        Assert.Equal(2, result.Sources.Count);
        Assert.Equal(("haandbog.md", "Ferie", 0.81), (result.Sources[0].Source, result.Sources[0].Heading, result.Sources[0].Score));
        Assert.Equal("it.md", result.Sources[1].Source);
    }

    [Fact]
    public async Task SendAsync_gemmer_kun_spoergsmaal_og_svar_i_historikken()
    {
        var store = new InMemoryConversationStore();
        var service = new ChatService(
            new FakeChatClient("Svar"),
            store,
            [],
            new RetrievalContext(),
            Options.Create(new ChatbotOptions { SystemPrompt = SystemPrompt }),
            NullLogger<ChatService>.Instance);

        var result = await service.SendAsync(new ChatTurnRequest("Spørgsmål"));

        var history = await store.GetAsync(result.ConversationId);
        Assert.Equal(2, history.Count);
        Assert.Equal(ChatRole.User, history[0].Role);
        Assert.Equal(ChatRole.Assistant, history[1].Role);
    }

    private static DocumentChunk Chunk(string source, string heading, int index)
        => new($"{source}#{index}", source, heading, index, "indhold", ReadOnlyMemory<float>.Empty);

    private sealed class StubToolProvider(string name) : IChatToolProvider
    {
        public IReadOnlyList<AITool> GetTools() => [AIFunctionFactory.Create(() => "ok", name)];
    }
}
