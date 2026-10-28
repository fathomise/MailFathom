// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Trust;

/// <summary>Reads, under the address lock, which contacts the books a receiving account reaches hold for a set of senders.</summary>
/// <remarks>
/// <para>
/// The books a receiving account reaches are its organization's and the own book of every user it is assigned to, as
/// they read when the message is judged. A mailbox's collected book is never read: collection writes down whoever wrote
/// to the mailbox, forgers included, so nothing it holds may make a sender trusted.
/// </para>
/// <para>
/// The lock comes first and the reading second, inside the session's transaction, which is what lets a contact write
/// and a verdict never interleave: every write to a book takes the same lock for each address it changes, so this reads
/// the books wholly before such a write or wholly after it.
/// </para>
/// </remarks>
public interface ISenderTrustBooks
{
    /// <summary>Takes the address lock for every sender, then reads the contacts holding any of them in the books each account reaches.</summary>
    /// <param name="session">The session whose transaction holds the locks and in which the verdicts will be written.</param>
    /// <param name="accounts">The receiving accounts whose books are read.</param>
    /// <param name="senders">The first <c>From</c> mailbox of each message being judged.</param>
    /// <param name="cancellationToken">Cancels the wait for a lock and the reads.</param>
    /// <returns>Every holding, whatever wrote its address; deciding which of them count is the caller's.</returns>
    Task<IReadOnlyList<SenderAddressHolding>> LockAndReadAsync(
        IPersistenceSession session,
        IReadOnlyCollection<MailAccountId> accounts,
        IReadOnlyCollection<EmailAddress> senders,
        CancellationToken cancellationToken);
}
