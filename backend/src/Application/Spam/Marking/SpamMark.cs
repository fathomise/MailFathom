// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Spam;

namespace MailFathom.Application.Spam.Marking;

/// <summary>What the person says a message is: spam, with or without the kind of spam it is, or not spam.</summary>
/// <remarks>
/// It is the person's own verdict and outranks every stage, so it is stated rather than inferred: not spam is the person
/// saying the message is ordinary mail, never the absence of a spam verdict, and it carries no subcategory.
/// </remarks>
public sealed record SpamMark
{
    private SpamMark(SpamVerdict verdict, SpamSubcategory? subcategory)
    {
        this.Verdict = verdict;
        this.Subcategory = subcategory;
    }

    /// <summary>Gets the mark saying the message is ordinary mail.</summary>
    public static SpamMark NotSpam { get; } = new(SpamVerdict.NotSpam, subcategory: null);

    /// <summary>Gets the verdict the mark records, which is spam or not spam and never undetermined.</summary>
    public SpamVerdict Verdict { get; }

    /// <summary>Gets what kind of spam the person named, or <see langword="null" /> where they named none or the mark is not spam.</summary>
    public SpamSubcategory? Subcategory { get; }

    /// <summary>Builds the mark saying the message is spam.</summary>
    /// <param name="subcategory">What kind of spam the person named, or <see langword="null" /> where they dismissed the choice.</param>
    /// <returns>The mark.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="subcategory" /> is not a defined member.</exception>
    public static SpamMark Spam(SpamSubcategory? subcategory) =>
        subcategory is { } named && !Enum.IsDefined(named)
            ? throw new ArgumentOutOfRangeException(nameof(subcategory), named, "A spam mark names a subcategory this system declares.")
            : new SpamMark(SpamVerdict.Spam, subcategory);
}
