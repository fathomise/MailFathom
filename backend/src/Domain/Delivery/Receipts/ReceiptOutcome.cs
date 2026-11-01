// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Delivery.Receipts;

/// <summary>States what one report said about one recipient of a message.</summary>
/// <remarks>
/// The first five are the <c>Action</c> values RFC 3464 defines for a delivery status notification, and the last four the
/// disposition types RFC 8098 defines for a read receipt. They are one set rather than two, because a recipient's outcome
/// is always read together with which report gave it, and <see cref="ReceiptOutcomes.KindOf" /> recovers that from the
/// value alone.
/// </remarks>
public enum ReceiptOutcome
{
    /// <summary>The message reached the recipient's mailbox.</summary>
    Delivered = 0,

    /// <summary>The message was passed on to a system that sends no delivery notification of its own.</summary>
    Relayed = 1,

    /// <summary>The message reached an address that expanded it to several others, such as a list.</summary>
    Expanded = 2,

    /// <summary>The message could not be delivered and will not be tried again.</summary>
    Failed = 3,

    /// <summary>The message has not been delivered yet and is still being tried.</summary>
    Delayed = 4,

    /// <summary>The recipient's mail client showed the message.</summary>
    Displayed = 5,

    /// <summary>The message was deleted without being shown.</summary>
    Deleted = 6,

    /// <summary>The message was sent somewhere else, such as by forwarding, without being shown.</summary>
    Dispatched = 7,

    /// <summary>The message was processed by something other than a person, without being shown.</summary>
    Processed = 8,
}
