// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Mail.Delivery.Receipts;

/// <summary>Finds the account's own message a report names, and records what an arriving or an opened message says about receipts.</summary>
public interface IReceiptReportStore
{
    /// <summary>Finds one of the account's own outgoing emails by its identifier.</summary>
    /// <param name="account">The account the report arrived in, which is the only account whose sends it may name.</param>
    /// <param name="outgoingEmailId">The identifier the report's <c>Original-Envelope-Id</c> carried.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The outgoing email, or <see langword="null" /> where the account sent none by that identifier.</returns>
    Task<ReportedOutgoingEmail?> FindOutgoingEmailAsync(
        MailAccountId account,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken);

    /// <summary>Finds the account's own outgoing email that carried one of several <c>Message-ID</c> values.</summary>
    /// <param name="account">The account the report arrived in.</param>
    /// <param name="internetMessageIds">The identifiers to try, in the order they are preferred.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The outgoing email carrying the earliest identifier in the list that names one, or <see langword="null" />.</returns>
    Task<ReportedOutgoingEmail?> FindOutgoingEmailByMessageIdAsync(
        MailAccountId account,
        IReadOnlyList<string> internetMessageIds,
        CancellationToken cancellationToken);

    /// <summary>Records what an arriving message said about receipts, in the transaction that stores it.</summary>
    /// <param name="session">The transaction storing the message.</param>
    /// <param name="storedEmailId">The message just stored.</param>
    /// <param name="receipts">What it is, what it asks, and what it changes.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <remarks>
    /// Each outcome is applied under <see cref="Domain.Delivery.Receipts.ReceiptOutcomes.Replaces" />, so recording the same
    /// message twice — a retried commit, or the same report met again — leaves what the first recording left. An
    /// organization's delivery is recorded against the message just stored as well as against the send, so it is erased
    /// with that message.
    /// </remarks>
    Task RecordArrivalAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        ArrivedReceipts receipts,
        CancellationToken cancellationToken);

    /// <summary>Records that a person opened messages an organization's delivery was recorded on, as their read.</summary>
    /// <param name="session">The transaction to record in.</param>
    /// <param name="opened">The messages the person opened.</param>
    /// <param name="openedAt">When they opened them.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when every read is staged.</returns>
    /// <remarks>
    /// A message no organization's delivery was recorded on records nothing, so opening any other mail reports nothing
    /// to anybody. Each read is applied under <see cref="Domain.Delivery.Receipts.ReceiptOutcomes.Replaces" />, so opening
    /// a message again keeps the first read.
    /// </remarks>
    Task RecordOpenedAsync(
        IPersistenceSession session,
        IReadOnlyCollection<StoredEmailId> opened,
        DateTimeOffset openedAt,
        CancellationToken cancellationToken);

    /// <summary>Claims the one read receipt a stored message may be answered with.</summary>
    /// <param name="storedEmailId">The message the receipt answers.</param>
    /// <param name="sentAt">The time to record the receipt as sent at.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>
    /// <see langword="null" /> where this call claimed it, or the time an earlier claim recorded. Two replicas claiming
    /// at once are decided by one conditional statement, so exactly one of them gets <see langword="null" />.
    /// </returns>
    Task<DateTimeOffset?> ClaimReadReceiptAsync(
        StoredEmailId storedEmailId,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken);

    /// <summary>Gives back a claim whose receipt was then refused before it was written down to be sent.</summary>
    /// <param name="storedEmailId">The message the receipt answers.</param>
    /// <param name="sentAt">The time the claim recorded, which is what keeps this from releasing somebody else's.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the claim is released.</returns>
    Task ReleaseReadReceiptAsync(
        StoredEmailId storedEmailId,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken);
}
