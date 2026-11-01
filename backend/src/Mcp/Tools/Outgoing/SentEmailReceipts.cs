// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel;
using MailFathom.Domain.Delivery.Receipts;

namespace MailFathom.Mcp.Tools.Outgoing;

/// <summary>Publishes what the account's own message asked to be told and what came back, recipient by recipient.</summary>
/// <remarks>
/// Read from the outcomes recorded when the reports arrived, never from the reports themselves, which no tool here reads.
/// It is absent from a message that asked for no report and got none, so its presence with no outcome says
/// <em>asked, nothing back yet</em>.
/// </remarks>
internal sealed record SentEmailReceipts
{
    /// <summary>Gets whether the message asked for a delivery notification.</summary>
    [Description("Whether the message asked for a delivery notification (RFC 3461).")]
    public required bool DeliveryRequested { get; init; }

    /// <summary>Gets whether the message asked for a read receipt.</summary>
    [Description("Whether the message asked for a read receipt (RFC 8098).")]
    public required bool ReadRequested { get; init; }

    /// <summary>Gets what came back for each recipient.</summary>
    [Description("Every recipient the message was sent to, with the delivery and read outcomes recorded for each.")]
    public required IReadOnlyList<RecipientReceiptsResult> Recipients { get; init; }

    /// <summary>Publishes a message's receipts, or nothing where it asked for none and got none.</summary>
    /// <param name="receipts">The receipts read with the message.</param>
    /// <returns>The wire representation, or <see langword="null" /> where <paramref name="receipts" /> is.</returns>
    public static SentEmailReceipts? From(OutgoingEmailReceipts? receipts) => receipts is null
        ? null
        : new SentEmailReceipts
        {
            DeliveryRequested = receipts.Requests.DeliveryNotification,
            ReadRequested = receipts.Requests.ReadReceipt,
            Recipients = [.. receipts.Recipients.Select(static recipient => new RecipientReceiptsResult
            {
                Address = recipient.Address,
                Delivery = ReceiptOutcomeResult.From(recipient.Delivery),
                Read = ReceiptOutcomeResult.From(recipient.Read),
            })],
        };
}
