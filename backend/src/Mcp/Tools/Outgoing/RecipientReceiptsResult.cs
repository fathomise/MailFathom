// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel;

namespace MailFathom.Mcp.Tools.Outgoing;

/// <summary>Publishes what came back about one recipient of the account's own message.</summary>
internal sealed record RecipientReceiptsResult
{
    /// <summary>Gets the recipient's address.</summary>
    [Description("The recipient's mail address, as the message was sent to it.")]
    public required string Address { get; init; }

    /// <summary>Gets what a delivery report said.</summary>
    [Description("What a delivery report said about this recipient, or absent where none came back.")]
    public ReceiptOutcomeResult? Delivery { get; init; }

    /// <summary>Gets what a read receipt said.</summary>
    [Description("What a read receipt said about this recipient, or absent where none came back. Most mail apps send none, so absence does not mean unread.")]
    public ReceiptOutcomeResult? Read { get; init; }
}
