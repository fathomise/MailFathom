// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Emails.Signatures;

/// <summary>How a signer's revocation status was established.</summary>
public enum RevocationCheckMethod
{
    /// <summary>The OCSP responder the certificate names answered.</summary>
    Ocsp = 0,

    /// <summary>The certificate revocation list the certificate's distribution point names was read.</summary>
    Crl = 1,

    /// <summary>The configured OpenPGP key was read for a revocation signature on itself.</summary>
    KeyRevocationSignature = 2,
}
