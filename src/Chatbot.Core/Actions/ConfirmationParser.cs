using System.Text.RegularExpressions;

namespace Chatbot.Core.Actions;

public enum ConfirmationIntent
{
    /// <summary>Beskeden er hverken et klart ja eller nej — f.eks. en rettelse eller et nyt spørgsmål.</summary>
    Unclear,
    Confirm,
    Reject,
}

/// <summary>
/// Afgør deterministisk, om brugerens svar på "bekræft?" er ja, nej eller noget tredje.
/// Kun korte, entydige svar tælles: "ja", "ja tak", "bekræft", "nej", "annuller". Alt andet —
/// "ja, men e-mailen skal være..." — er <see cref="ConfirmationIntent.Unclear"/> og går til modellen,
/// så brugeren kan rette oplysningerne uden at starte forfra (fase 3.3).
///
/// Bevidst regelbaseret og ikke et modelkald: udførelsen af en handling må ikke afhænge af, om
/// en 8B-model tolker "ja" rigtigt — og en prompt injection i et dokument må ikke kunne udløse den.
/// </summary>
public static partial class ConfirmationParser
{
    public static ConfirmationIntent Parse(string message)
    {
        var text = Normalize(message);
        if (text.Length == 0 || text.Length > 40)
        {
            return ConfirmationIntent.Unclear;
        }

        if (ConfirmPattern().IsMatch(text))
        {
            return ConfirmationIntent.Confirm;
        }

        if (RejectPattern().IsMatch(text))
        {
            return ConfirmationIntent.Reject;
        }

        return ConfirmationIntent.Unclear;
    }

    private static string Normalize(string message)
        => Regex.Replace(message.Trim().ToLowerInvariant(), @"[.!,]+$", string.Empty).Trim();

    [GeneratedRegex(@"^(ja|jep|jo|yes|ok|okay|bekræft|bekræfter|bekræftet|gør det|kør|fortsæt|opret|det er korrekt|korrekt|ja tak|ja, tak|ja gør det|ja, gør det|ja bekræft|ja, bekræft)$")]
    private static partial Regex ConfirmPattern();

    [GeneratedRegex(@"^(nej|nej tak|nej, tak|no|stop|annuller|annullér|afbryd|glem det|drop det|lad være|nej lad være|nej, lad være)$")]
    private static partial Regex RejectPattern();
}
