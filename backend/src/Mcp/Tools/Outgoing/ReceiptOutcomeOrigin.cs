// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel;

namespace MailFathom.Mcp.Tools.Outgoing;

/// <summary>Publishes where a recorded outcome came from.</summary>
internal enum ReceiptOutcomeOrigin
{
    /// <summary>A report that arrived as mail.</summary>
    [Description("A report that came back as mail. Anybody could have written one, so it is information rather than proof.")]
    Report = 0,

    /// <summary>This deployment itself, between two accounts of one organization.</summary>
    [Description("This deployment saw it itself, because the recipient is another account of the sender's organization: the message was stored in that account, or a person opened it there. It is proof, and it takes precedence over any report.")]
    Organization = 1,
}
