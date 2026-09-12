using Chatbot.Core.Chat;

namespace Chatbot.Tests;

public class ReplySanitizerTests
{
    [Fact]
    public void Unwrap_pakker_llama_message_objekt_ud()
    {
        var reply = """{"type":"message","text":"Det kan jeg ikke finde i dokumentationen."}""";

        Assert.Equal("Det kan jeg ikke finde i dokumentationen.", ReplySanitizer.Unwrap(reply));
    }

    [Theory]
    [InlineData("""{"content":"Hej"}""", "Hej")]
    [InlineData("""  {"message":"Hej"}  """, "Hej")]
    public void Unwrap_kender_flere_feltnavne(string reply, string expected)
        => Assert.Equal(expected, ReplySanitizer.Unwrap(reply));

    [Theory]
    [InlineData("Almindelig tekst.")]
    [InlineData("{ikke json}")]
    [InlineData("""{"type":"message"}""")]
    [InlineData("""{"text":""}""")]
    [InlineData("""[{"text":"liste"}]""")]
    [InlineData("")]
    public void Unwrap_lader_alt_andet_vaere(string reply)
        => Assert.Equal(reply, ReplySanitizer.Unwrap(reply));
}
