// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Settlement;

/// <summary>Reads the messages of one account that are still settling, and writes down the ones that settled.</summary>
public interface IMailSettlementStore
{
    /// <summary>Reads one batch of the account's settling messages, in identity order.</summary>
    /// <param name="account">The account whose arrivals are read.</param>
    /// <param name="resumeAfter">The last message the walk already read, or <see langword="null" /> to start at the head.</param>
    /// <param name="batchSize">The most messages the batch holds.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The batch, empty once the walk has passed the last settling message.</returns>
    Task<IReadOnlyList<SettlingEmail>> ReadSettlingAsync(
        MailAccountId account,
        StoredEmailId? resumeAfter,
        int batchSize,
        CancellationToken cancellationToken);

    /// <summary>Settles the named messages that are still settling, and reports which of them this call settled.</summary>
    /// <param name="emails">The messages to settle.</param>
    /// <param name="settledAt">The instant written as the settlement.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The messages this call settled, leaving out any a competing run had already settled.</returns>
    /// <remarks>
    /// One conditional statement, so two runs settling the same message settle it once and only the one that did reports
    /// it — which is what keeps the arrival signal and the arrival notification from being raised twice.
    /// </remarks>
    Task<IReadOnlyList<StoredEmailId>> SettleAsync(
        IReadOnlyList<StoredEmailId> emails,
        DateTimeOffset settledAt,
        CancellationToken cancellationToken);
}
