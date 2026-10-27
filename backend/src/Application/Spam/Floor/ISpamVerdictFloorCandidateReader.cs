// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Spam.Floor;

/// <summary>Finds the stored mail of one account that sits below the spam floor.</summary>
public interface ISpamVerdictFloorCandidateReader
{
    /// <summary>Reads the next batch of messages in a junk folder or carrying <c>$Junk</c> that hold no spam verdict.</summary>
    /// <param name="account">The account whose mail is read.</param>
    /// <param name="terms">The junk folders and the classification wait the pass is decided under.</param>
    /// <param name="resumeAfter">The last message the pass already reached, or <see langword="null" /> to start at the beginning.</param>
    /// <param name="batchSize">The most messages to return.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The candidates in identity order, never more than <paramref name="batchSize" />.</returns>
    /// <remarks>
    /// A message carrying the person's own verdict, one already decided spam, one whose <c>$NotJunk</c> stands alone
    /// among the keywords somebody other than this deployment's verdict set, and one its classification still awaits
    /// under <paramref name="terms" /> are not candidates. A message the source no longer holds is, because its local
    /// copy is still mail a person reads.
    /// </remarks>
    Task<IReadOnlyList<SpamVerdictFloorCandidate>> GetCandidatesAsync(
        MailAccountId account,
        SpamVerdictFloorTerms terms,
        StoredEmailId? resumeAfter,
        int batchSize,
        CancellationToken cancellationToken);
}
