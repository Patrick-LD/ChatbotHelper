using Chatbot.Core.Actions;
using Chatbot.Core.Chat;
using Chatbot.Core.Rag;
using Chatbot.Core.Tools;
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
        RetrievalContext? retrieved = null,
        IConversationStore? store = null,
        IPendingActionStore? pending = null,
        IEnumerable<IActionExecutor>? executors = null,
        int pendingTimeoutMinutes = 30)
        => new(
            client,
            store ?? new InMemoryConversationStore(),
            tools ?? [],
            executors ?? [],
            pending ?? new InMemoryPendingActionStore(),
            retrieved ?? new RetrievalContext(),
            new TurnContext(),
            Options.Create(new ChatbotOptions
            {
                SystemPrompt = SystemPrompt,
                MaxHistoryMessages = maxHistoryMessages,
            }),
            Options.Create(new ToolsOptions { PendingActionTimeoutMinutes = pendingTimeoutMinutes }),
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
        var service = CreateService(new FakeChatClient("Svar"), store: store);

        var result = await service.SendAsync(new ChatTurnRequest("Spørgsmål"));

        var history = await store.GetAsync(result.ConversationId);
        Assert.Equal(2, history.Count);
        Assert.Equal(ChatRole.User, history[0].Role);
        Assert.Equal(ChatRole.Assistant, history[1].Role);
    }

    // ---- Bekræftelses-flow (fase 3.3) ----

    private static PendingAction Pending(DateTimeOffset? createdAt = null)
        => new("test_tool", "Opret Lars Hansen", """{"name":"Lars"}""", createdAt ?? DateTimeOffset.UtcNow);

    [Fact]
    public async Task SendAsync_med_ventende_handling_og_ja_udfoerer_uden_modelkald()
    {
        var client = new FakeChatClient();
        var pending = new InMemoryPendingActionStore();
        await pending.SetAsync("c1", Pending());
        var executor = new StubExecutor("test_tool");
        var service = CreateService(client, pending: pending, executors: [executor]);

        var result = await service.SendAsync(new ChatTurnRequest("ja", "c1"));

        Assert.Equal(0, client.CallCount);
        Assert.Equal("""{"name":"Lars"}""", executor.ExecutedWith);
        Assert.Equal("Udført!", result.Reply);
        Assert.Null(result.PendingAction);
        Assert.Null(await pending.GetAsync("c1"));
    }

    [Fact]
    public async Task SendAsync_med_ventende_handling_og_nej_annullerer_uden_modelkald()
    {
        var client = new FakeChatClient();
        var pending = new InMemoryPendingActionStore();
        await pending.SetAsync("c1", Pending());
        var executor = new StubExecutor("test_tool");
        var service = CreateService(client, pending: pending, executors: [executor]);

        var result = await service.SendAsync(new ChatTurnRequest("nej", "c1"));

        Assert.Equal(0, client.CallCount);
        Assert.Null(executor.ExecutedWith);
        Assert.Contains("annulleret", result.Reply);
        Assert.Null(await pending.GetAsync("c1"));
    }

    [Fact]
    public async Task SendAsync_med_ventende_handling_og_uklar_besked_gaar_til_modellen_med_note()
    {
        var client = new FakeChatClient();
        var pending = new InMemoryPendingActionStore();
        await pending.SetAsync("c1", Pending());
        var executor = new StubExecutor("test_tool");
        var service = CreateService(client, pending: pending, executors: [executor]);

        var result = await service.SendAsync(new ChatTurnRequest("e-mailen skal være lars@firma.dk", "c1"));

        Assert.Equal(1, client.CallCount);
        Assert.Null(executor.ExecutedWith);
        Assert.Contains(client.LastPrompt, m => m.Role == ChatRole.System && m.Text.Contains("Opret Lars Hansen"));
        Assert.Equal("Opret Lars Hansen", result.PendingAction);
    }

    [Fact]
    public async Task SendAsync_ja_efter_udloebet_handling_udfoerer_ikke()
    {
        var client = new FakeChatClient();
        var pending = new InMemoryPendingActionStore();
        await pending.SetAsync("c1", Pending(DateTimeOffset.UtcNow.AddMinutes(-31)));
        var executor = new StubExecutor("test_tool");
        var service = CreateService(client, pending: pending, executors: [executor], pendingTimeoutMinutes: 30);

        await service.SendAsync(new ChatTurnRequest("ja", "c1"));

        Assert.Null(executor.ExecutedWith);
        Assert.Equal(1, client.CallCount);
        Assert.Null(await pending.GetAsync("c1"));
    }

    [Fact]
    public async Task SendAsync_ja_uden_ventende_handling_er_bare_en_besked()
    {
        var client = new FakeChatClient();
        var service = CreateService(client);

        await service.SendAsync(new ChatTurnRequest("ja", "c1"));

        Assert.Equal(1, client.CallCount);
    }

    private static DocumentChunk Chunk(string source, string heading, int index)
        => new($"{source}#{index}", source, heading, index, "indhold", ReadOnlyMemory<float>.Empty);

    private sealed class StubToolProvider(string name) : IChatToolProvider
    {
        public IReadOnlyList<AITool> GetTools() => [AIFunctionFactory.Create(() => "ok", name)];
    }

    private sealed class StubExecutor(string toolName) : IActionExecutor
    {
        public string ToolName => toolName;

        public string? ExecutedWith { get; private set; }

        public Task<string> ExecuteAsync(string parametersJson, CancellationToken cancellationToken = default)
        {
            ExecutedWith = parametersJson;
            return Task.FromResult("Udført!");
        }
    }
}
