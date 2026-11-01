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
}
