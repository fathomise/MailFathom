// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Evaluations.Languages;

/// <summary>Tells whether a drafted message closes itself, in either language MailFathom writes in.</summary>
/// <remarks>
/// <para>
/// A draft from an account that appends the person's footer has to end at its own words, because the footer is the
/// closing; a draft from any other account has to close as a person would. Both are a structure a phrase settles, so
/// no judge is asked: a sign-off is a closing phrase standing on one of the last lines of the message, which is where a
/// closing stands and where a sentence that merely thanks somebody does not.
/// </para>
/// <para>
/// A word that closes only when it stands alone — <c>Thanks</c>, or the name the person signs their own mail with —
/// is a closing only as the whole line. Found inside a line, the same word opens the sentence
/// <c>Thanks for the update.</c>, which is short enough to stand among the last lines and closes nothing.
/// </para>
/// <para>
/// The lists are the closings the models measured here write. A closing they miss passes a footer case it should fail;
/// they are widened then, never the check.
/// </para>
/// </remarks>
internal static class SignOff
{
    /// <summary>The name the check that a draft closes, or leaves its closing to the footer, is recorded under.</summary>
    public const string MetricName = "Closes as its account's footer requires";

    /// <summary>How many of a message's last non-empty lines a closing is looked for on.</summary>
    private const int ClosingLines = 3;

    /// <summary>The longest line, in characters, read as a closing rather than as a sentence of the message.</summary>
    /// <remarks>
    /// A closing with the name on its line — <c>Best regards, Mara Nowak</c> — fits; a last sentence that opens on
    /// thanks, as <c>Thank you, Zofia, for checking the corrected invoice.</c> does, does not.
    /// </remarks>
    private const int LongestClosingLine = 30;

    private static readonly string[] ClosingPhrases =
    [
        "regards",
        "best wishes",
        "all the best",
        "sincerely",
        "cheers",
        "many thanks",
        "thanks,",
        "thank you,",
        "best,",
        "pozdrawiam",
        "pozdrowienia",
        "z poważaniem",
        "dziękuję,",
        "dzięki,",
        "serdecznie",
    ];

    private static readonly string[] StandaloneClosings = ["thanks", "thank you", "dzięki", "dziękuję"];

    /// <summary>Reads the closings the person's own mail ends with: the last line of each message, where it is short enough to be one.</summary>
    /// <param name="sentMail">What the person wrote in messages of their own, each with any quoted history already left out.</param>
    /// <returns>The closings, without duplicates, for <see cref="Shortfall" /> to recognise as the whole of a line.</returns>
    public static IReadOnlyList<string> ClosingsOf(IEnumerable<string> sentMail) =>
    [
        .. sentMail
            .Select(static text => LinesOf(text).LastOrDefault())
            .OfType<string>()
            .Where(static line => line.Length <= LongestClosingLine && Unpunctuated(line).Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase),
    ];

    /// <summary>Reads the closing a message ends with, if it ends with one.</summary>
    /// <param name="body">The message as a draft writes it.</param>
    /// <param name="ownClosings">The closings the person's own mail ends with, each a closing only as the whole of a line.</param>
    /// <returns>The line that closes it, or <see langword="null" /> where it ends at its own words.</returns>
    public static string? Of(string? body, IReadOnlyCollection<string> ownClosings) =>
        LinesOf(body)
            .TakeLast(ClosingLines)
            .FirstOrDefault(line =>
                line.Length <= LongestClosingLine
                && (ClosingPhrases.Any(phrase => line.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                    || StandaloneClosings.Concat(ownClosings).Any(closing => string.Equals(
                        Unpunctuated(line),
                        Unpunctuated(closing),
                        StringComparison.OrdinalIgnoreCase))));

    /// <summary>States what is wrong with how a message closes, given whether its account appends the person's footer.</summary>
    /// <param name="body">The message as a draft writes it.</param>
    /// <param name="closesWithFooter">Whether the account the message is sent from appends the person's footer.</param>
    /// <param name="ownClosings">The closings the person's own mail ends with, which <see cref="ClosingsOf" /> reads.</param>
    /// <returns>The shortfall, or <see langword="null" /> where the message closes as it should.</returns>
    public static string? Shortfall(
        string? body,
        bool closesWithFooter,
        IReadOnlyCollection<string> ownClosings) => (Of(body, ownClosings), closesWithFooter) switch
        {
            ({ } closing, true) => $"The draft closes itself with \"{closing}\" although its account's footer carries the closing.",
            (null, false) => "The draft ends without a closing although its account appends no footer.",
            _ => null,
        };

    private static string[] LinesOf(string? text) =>
        (text ?? string.Empty)
            .ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static string Unpunctuated(string line) => line.TrimEnd('.', ',', '!', ';', ':', ' ');
}
