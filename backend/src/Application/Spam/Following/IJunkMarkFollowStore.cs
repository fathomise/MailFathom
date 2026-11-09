// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Spam.Following;

/// <summary>A change to one stored email's junk marks that still has to reach the classification queue.</summary>
/// <param name="EmailId">The stored email.</param>
/// <param name="ObservedAt">
/// When the change was observed, or when the email was stored where the marks were the ones it arrived with, which is
/// earlier than any verdict a person can have given it.
/// </param>
public sealed record OwedJunkMarkChange(StoredEmailId EmailId, DateTimeOffset ObservedAt);

/// <summary>Keeps what following the source's junk marks still owes, and what another client's move out of junk left behind.</summary>
public interface IJunkMarkFollowStore
{
    /// <summary>Reads the changes of one account that have not reached the queue yet.</summary>
    /// <param name="account">The account whose mail is read.</param>
    /// <param name="batchSize">The most changes to return.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The owed changes in identity order, never more than <paramref name="batchSize" />.</returns>
    /// <remarks>Settling a change is what takes it out of this read, so no resume position travels with a batch.</remarks>
    Task<IReadOnlyList<OwedJunkMarkChange>> GetOwedAsync(
        MailAccountId account,
        int batchSize,
        CancellationToken cancellationToken);

    /// <summary>Records that one change has reached the queue.</summary>
    /// <param name="change">The change, as it was read.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the write is committed.</returns>
    /// <remarks>Conditional on the value read, so a newer change observed meanwhile stays owed.</remarks>
    Task SettleAsync(OwedJunkMarkChange change, CancellationToken cancellationToken);

    /// <summary>Forgets the departures from the junk folder recorded before an instant.</summary>
    /// <param name="account">The account whose departures are forgotten.</param>
    /// <param name="before">The oldest departure kept.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the write is committed.</returns>
    Task ForgetJunkFolderDeparturesAsync(
        MailAccountId account,
        DateTimeOffset before,
        CancellationToken cancellationToken);
}
