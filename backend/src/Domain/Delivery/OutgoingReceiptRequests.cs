// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Delivery;

/// <summary>States which of the two reports a sender asked to be told about one message, both of which are off unless asked for.</summary>
/// <param name="DeliveryNotification">
/// Whether the sender asked for a delivery status notification (RFC 3461). It is envelope data rather than part of the
/// message, so it is carried from the draft to the send and written into the SMTP envelope when the message is transmitted.
/// </param>
/// <param name="ReadReceipt">
/// Whether the sender asked for a read receipt (RFC 8098). It is a header of the message itself, so it is written when the
/// message is composed and travels in the stored MIME; the value here records that it was asked for.
/// </param>
/// <remarks>
/// The two are asked independently and travel differently, which is why they are two values rather than one level: a
/// recipient's server answers the first, and the recipient's mail client — or the recipient — the second.
/// </remarks>
public readonly record struct OutgoingReceiptRequests(bool DeliveryNotification, bool ReadReceipt)
{
    /// <summary>Gets the requests of a message that asked for neither report, which is what every message asks for unless its sender says otherwise.</summary>
    public static OutgoingReceiptRequests None { get; } = new(DeliveryNotification: false, ReadReceipt: false);
}
