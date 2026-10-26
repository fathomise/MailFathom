// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Spam;

namespace MailFathom.Application.Emails.Summaries;

/// <summary>What spam classification concluded about one stored message, as far as a screen draws it.</summary>
/// <param name="Verdict">What the classification concluded.</param>
/// <param name="DecidedBy">Which stage reached the verdict, the person's own included.</param>
/// <param name="Subcategory">What kind of spam the message is, or <see langword="null" /> where no stage named one.</param>
/// <param name="PhishingRequest">What a phishing message asks its reader for, or <see langword="null" /> where the model named nothing.</param>
/// <param name="ImpersonatedParty">The kind of party the message pretends to come from, or <see langword="null" /> where the model named nothing.</param>
/// <remarks>
/// <para>
/// A projection of the recorded <see cref="SpamClassification" /> rather than the record itself: the score, the
/// profile, and the signals are what the verdict was reached from, and a message list reads none of them. Every value
/// here is a closed one, so nothing in it is text a model or a sender wrote.
/// </para>
/// <para>
/// It is a judgement about one person's message and is personal data like the message it describes.
/// </para>
/// </remarks>
public sealed record EmailSpamVerdict(
    SpamVerdict Verdict,
    SpamClassificationStage DecidedBy,
    SpamSubcategory? Subcategory,
    SpamPhishingRequest? PhishingRequest,
    SpamImpersonatedParty? ImpersonatedParty)
{
    /// <summary>Gets which stage named the subcategory, or <see langword="null" /> where there is none.</summary>
    /// <remarks>Answered the way <see cref="SpamClassification.SubcategoryNamedBy" /> answers it, from the deciding stage.</remarks>
    public SpamClassificationStage? SubcategoryNamedBy =>
        SpamClassification.StageNamingSubcategory(this.DecidedBy, this.Subcategory);
}
