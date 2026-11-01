// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Mail.Delivery.Receipts;

/// <summary>States how a reader's request to send a read receipt ended.</summary>
public enum ReadReceiptSendingOutcome
{
    /// <summary>The receipt was written down to be sent.</summary>
    Sent = 0,

    /// <summary>The caller reads no such message, which is answered the same whether it exists or not.</summary>
    NotFound = 1,

    /// <summary>The message asked for no read receipt.</summary>
    NotRequested = 2,

    /// <summary>The message is spam, and answering it would confirm to its sender that the address reads mail.</summary>
    Spam = 3,

    /// <summary>The message is the account's own, which nobody here answers.</summary>
    OwnMessage = 4,

    /// <summary>A receipt was already sent for the message, by this user or another assigned to the account.</summary>
    AlreadySent = 5,

    /// <summary>The account configures no address to send from.</summary>
    SenderUnconfigured = 6,

    /// <summary>The receipt could not be composed for the address the message named, such as one too long or unusable.</summary>
    Unaddressable = 7,
}
