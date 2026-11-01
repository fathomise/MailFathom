// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Delivery.Receipts;

/// <summary>States which of the two reports a message can ask for an outcome came from.</summary>
/// <remarks>
/// The two are answered by different parties and answer different questions, so a recipient carries at most one outcome
/// of each: a delivery status notification (RFC 3464) is written by a server on the way and says where the message got
/// to, and a read receipt (RFC 8098) is written by the recipient's mail client and says what happened to it there.
/// </remarks>
public enum ReceiptKind
{
    /// <summary>A delivery status notification, <c>multipart/report; report-type=delivery-status</c>.</summary>
    Delivery = 0,

    /// <summary>A read receipt, <c>multipart/report; report-type=disposition-notification</c>.</summary>
    Read = 1,
}
