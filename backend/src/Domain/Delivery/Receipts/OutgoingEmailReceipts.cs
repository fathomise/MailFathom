// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Delivery.Receipts;

/// <summary>Carries what the account's own message asked to be told and what came back, recipient by recipient.</summary>
/// <param name="Requests">Which reports the message asked for.</param>
/// <param name="Recipients">Every recipient the message was sent to, in the order it named them, with what came back for each.</param>
/// <remarks>
/// It exists only for a message that asked for a report or got one: <see cref="Of" /> answers nothing for a message with
/// neither, so a reader can tell <em>nothing back yet</em> from <em>nothing asked</em> by whether it is there at all.
/// </remarks>
public sealed record OutgoingEmailReceipts(OutgoingReceiptRequests Requests, IReadOnlyList<RecipientReceipts> Recipients)
{
    /// <summary>Collects what came back for one message, or nothing where it asked for no report and got none.</summary>
    /// <param name="requests">Which reports the message asked for.</param>
    /// <param name="recipients">Its recipients with what came back for each.</param>
    /// <returns>The receipts, or <see langword="null" /> where there is nothing to say.</returns>
    public static OutgoingEmailReceipts? Of(OutgoingReceiptRequests requests, IReadOnlyList<RecipientReceipts> recipients)
    {
        ArgumentNullException.ThrowIfNull(recipients);

        return requests == OutgoingReceiptRequests.None
            && recipients.All(static recipient => recipient.Delivery is null && recipient.Read is null)
                ? null
                : new OutgoingEmailReceipts(requests, recipients);
    }
}
