// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel;

namespace MailFathom.Mcp.Tools.Outgoing;

/// <summary>Publishes what one report said about one recipient of the account's own message.</summary>
internal enum ReceiptOutcomeKind
{
    /// <summary>A delivery report said the message reached the mailbox.</summary>
    [Description("A delivery report said the message reached the recipient's mailbox.")]
    Delivered = 0,

    /// <summary>A delivery report said the message was passed to a system that reports no further.</summary>
    [Description("A delivery report said the message was passed on to a system that sends no delivery report of its own.")]
    Relayed = 1,

    /// <summary>A delivery report said the address expanded to several others.</summary>
    [Description("A delivery report said the address expanded the message to several others, such as a list.")]
    Expanded = 2,

    /// <summary>A delivery report said the message could not be delivered.</summary>
    [Description("A delivery report said the message could not be delivered and will not be tried again.")]
    Failed = 3,

    /// <summary>A delivery report said the message is still being tried.</summary>
    [Description("A delivery report said the message has not been delivered yet and is still being tried.")]
    Delayed = 4,

    /// <summary>A read receipt said the message was shown.</summary>
    [Description("A read receipt said the recipient's mail app showed the message — it was read.")]
    Displayed = 5,

    /// <summary>A read receipt said the message was deleted unshown.</summary>
    [Description("A read receipt said the message was deleted without being shown.")]
    Deleted = 6,

    /// <summary>A read receipt said the message was sent elsewhere unshown.</summary>
    [Description("A read receipt said the message was sent somewhere else, such as by forwarding, without being shown.")]
    Dispatched = 7,

    /// <summary>A read receipt said something other than a person processed the message.</summary>
    [Description("A read receipt said something other than a person processed the message, without it being shown.")]
    Processed = 8,
}
