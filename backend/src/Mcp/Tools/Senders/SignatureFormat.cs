// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Mcp.Tools.Senders;

/// <summary>Reports how an email is signed, as the protocol spells it.</summary>
internal enum SignatureFormat
{
    /// <summary>An S/MIME signature in a separate part beside the content it signs.</summary>
    SecureMimeDetached = 0,

    /// <summary>An S/MIME body that wraps the content it signs.</summary>
    SecureMimeOpaque = 1,

    /// <summary>A PGP/MIME signature in a separate part beside the content it signs.</summary>
    OpenPgpMime = 2,
}
