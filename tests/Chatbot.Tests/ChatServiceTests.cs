using Chatbot.Core.Chat;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Chatbot.Tests;

public class ChatServiceTests
{
    private const string SystemPrompt = "Du er en testassistent.";

    private static ChatService CreateService(FakeChatClient client, int maxHistoryMessages = 20)
        => new(
            client,
            new InMemoryConversationStore(),
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
}
