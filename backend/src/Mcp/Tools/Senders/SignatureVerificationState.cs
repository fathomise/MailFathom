// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Mcp.Tools.Senders;

/// <summary>Reports what verifying an email's own signature found, as the protocol spells it.</summary>
/// <remarks>
/// The transport carries its own enumeration for the reason every other published one does: the member names are the
/// wire values, so they belong to the boundary that publishes them.
/// </remarks>
internal enum SignatureVerificationState
{
    /// <summary>The email carries no S/MIME or OpenPGP signature of its own.</summary>
    NotSigned = 0,

    /// <summary>The signature is sound, its signer is trusted here, and the signed content is unchanged.</summary>
    Verified = 1,

    /// <summary>The signed content does not match its signature: it changed after signing, or the signature is not the signer's.</summary>
    Failed = 2,

    /// <summary>The signature could not be verified here, for the reason published beside it.</summary>
    NotVerifiable = 3,
}
