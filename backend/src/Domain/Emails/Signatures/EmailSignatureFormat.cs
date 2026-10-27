// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Emails.Signatures;

/// <summary>The form a signed message arrived in.</summary>
/// <remarks>Inline OpenPGP in a text body is not one of them: only PGP/MIME is read.</remarks>
public enum EmailSignatureFormat
{
    /// <summary>S/MIME with a detached signature: <c>multipart/signed</c> carrying <c>application/pkcs7-signature</c>.</summary>
    SecureMimeDetached = 0,

    /// <summary>S/MIME wrapping the whole message: <c>application/pkcs7-mime</c>.</summary>
    SecureMimeOpaque = 1,

    /// <summary>OpenPGP as RFC 3156 PGP/MIME: <c>multipart/signed</c> carrying <c>application/pgp-signature</c>.</summary>
    OpenPgpMime = 2,
}
