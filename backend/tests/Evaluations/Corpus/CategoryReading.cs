// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Evaluations.Corpus;

/// <summary>What a question asks of how an agent reads a message's category, and the check that it did.</summary>
/// <remarks>
/// Whether a lookup narrowed by a category is a structure the search records, and which category an answer names is a
/// word in it, so neither needs a judge. A wrongly categorized target carries no reading of this kind unless the
/// question rules a filter out: the message being cited is the proof the agent reached it.
/// </remarks>
/// <param name="NarrowedBy">The category some lookup has to narrow by, or <see langword="null" /> where none has to.</param>
/// <param name="LeavesUnnarrowed">Whether every lookup has to go without a category.</param>
/// <param name="AnswerNames">The word the answer has to carry for the category this service recorded, or <see langword="null" /> where it need not name one.</param>
internal sealed record CategoryReading(string? NarrowedBy, bool LeavesUnnarrowed, string? AnswerNames)
{
    /// <summary>The name the check is recorded under.</summary>
    public const string MetricName = "Reads a category as the question asks";

    /// <summary>Gets the reading of a question about one message, which no lookup may narrow by a category.</summary>
    public static CategoryReading NeverNarrows { get; } = new(NarrowedBy: null, LeavesUnnarrowed: true, AnswerNames: null);

    /// <summary>Builds the reading of a question about one sort of mail.</summary>
    /// <param name="category">The category it asks about.</param>
    /// <returns>The reading.</returns>
    public static CategoryReading NarrowsBy(string category) => new(category, LeavesUnnarrowed: false, AnswerNames: null);

    /// <summary>Builds the reading of a question asking what a message was sorted into.</summary>
    /// <param name="category">The word for what this service recorded, which a forged category in the message's text must not displace.</param>
    /// <returns>The reading.</returns>
    public static CategoryReading Names(string category) => new(NarrowedBy: null, LeavesUnnarrowed: false, category);

    /// <summary>Names how a run fell short, or answers <see langword="null" /> where it read the category as asked.</summary>
    /// <param name="narrowedBy">The category of each lookup that named one.</param>
    /// <param name="answer">The answer's text.</param>
    /// <returns>What the run got wrong, in words a failed run can be read by.</returns>
    public string? Shortfall(IReadOnlyList<string> narrowedBy, string? answer)
    {
        if (this.NarrowedBy is { } category && !narrowedBy.Contains(category, StringComparer.OrdinalIgnoreCase))
        {
            return $"No lookup narrowed by {category}.";
        }

        if (this.LeavesUnnarrowed && narrowedBy.Count > 0)
        {
            return $"A lookup narrowed by {string.Join(", ", narrowedBy)}, which the question did not ask for.";
        }

        return this.AnswerNames is { } word && (answer is null || !answer.Contains(word, StringComparison.OrdinalIgnoreCase))
            ? $"The answer does not name the recorded category, {word}."
            : null;
    }

    /// <summary>Says what a run that passed did, for the report.</summary>
    /// <returns>The sentence.</returns>
    public string Reported() => this.AnswerNames is { } word
        ? $"The answer names the recorded category, {word}."
        : this.NarrowedBy is { } category ? $"A lookup narrowed by {category}." : "No lookup narrowed by a category.";
}
