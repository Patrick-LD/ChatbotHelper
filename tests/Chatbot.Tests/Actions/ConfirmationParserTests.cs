using Chatbot.Core.Actions;

namespace Chatbot.Tests.Actions;

public class ConfirmationParserTests
{
    [Theory]
    [InlineData("ja")]
    [InlineData("Ja")]
    [InlineData("JA!")]
    [InlineData("ja tak")]
    [InlineData("Ja, tak.")]
    [InlineData("bekræft")]
    [InlineData("ok")]
    [InlineData("gør det")]
    [InlineData("  ja  ")]
    public void Parse_genkender_klart_ja(string message)
        => Assert.Equal(ConfirmationIntent.Confirm, ConfirmationParser.Parse(message));

    [Theory]
    [InlineData("nej")]
    [InlineData("Nej tak")]
    [InlineData("annuller")]
    [InlineData("stop")]
    [InlineData("glem det")]
    public void Parse_genkender_klart_nej(string message)
        => Assert.Equal(ConfirmationIntent.Reject, ConfirmationParser.Parse(message));

    [Theory]
    [InlineData("ja, men e-mailen skal være lars@firma.dk")]
    [InlineData("nej, afdelingen er Salg, ikke Marketing")]
    [InlineData("Hvad koster frokostordningen?")]
    [InlineData("måske")]
    [InlineData("")]
    [InlineData("jaaaa")]
    public void Parse_alt_andet_er_uklart(string message)
        => Assert.Equal(ConfirmationIntent.Unclear, ConfirmationParser.Parse(message));
}
