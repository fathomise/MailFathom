// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Spam.Actions;

/// <summary>Reads where a classified email currently is and whether it is already marked read.</summary>
/// <remarks>
/// A port of its own rather than a widening of <see cref="IClassifiableEmailReader" />, because the two are asked at
/// different moments and about different things: that one is asked before a verdict exists and answers what the verdict
/// is judged from, and this one is asked after and answers what a change would be issued against. Widening the first
/// would put a remote occurrence and a flag into every classification that never touches a mailbox.
/// </remarks>
public interface ISpamActionOccurrenceReader
{
    /// <summary>Finds the occurrence one local email currently has.</summary>
    /// <param name="emailId">The local email a classification was recorded for.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// Where the email is and how it stands, or <see langword="null" /> when there is nothing a change could be issued
    /// against: nothing is stored under that identifier, or the stored email carries no occurrence because no mail
    /// server holds it any longer.
    /// </returns>
    Task<SpamActionOccurrence?> FindAsync(StoredEmailId emailId, CancellationToken cancellationToken);

    /// <summary>Answers whether a message with this email's <c>Message-ID</c> recently left its account's junk folder through another mail client.</summary>
    /// <param name="emailId">The local email a filing is being decided for.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><see langword="true" /> when such a departure is on record; <see langword="false" /> otherwise, an email with no <c>Message-ID</c> included.</returns>
    /// <remarks>
    /// Another client's move creates a new stored email carrying none of the old row's record, and the <c>Message-ID</c> is
    /// the only thing the two share, so a message that carries none is not recognized.
    /// </remarks>
    Task<bool> HasLeftJunkFolderAsync(StoredEmailId emailId, CancellationToken cancellationToken);
}
