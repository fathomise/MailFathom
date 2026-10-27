// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Mcp.Tools.Senders;

/// <summary>Reports why an email's signature could not be verified here, as the protocol spells it.</summary>
internal enum SignatureUnverifiableReason
{
    /// <summary>The S/MIME signature carries no certificate for its signer.</summary>
    NoCertificate = 0,

    /// <summary>The signer's certificate does not chain to an authority this deployment trusts.</summary>
    UntrustedRoot = 1,

    /// <summary>The signer's certificate was not valid when the email was signed.</summary>
    OutsideValidityPeriod = 2,

    /// <summary>The signer's certificate is not issued for signing mail.</summary>
    WrongKeyUsage = 3,

    /// <summary>The signer's certificate is revoked.</summary>
    Revoked = 4,

    /// <summary>Whether the signer's certificate is revoked could not be established.</summary>
    RevocationNotCheckable = 5,

    /// <summary>The OpenPGP key that signed is not one this deployment trusts.</summary>
    UnknownKey = 6,

    /// <summary>The OpenPGP key that signed was expired when the email was signed.</summary>
    ExpiredKey = 7,

    /// <summary>The OpenPGP key that signed is revoked.</summary>
    RevokedKey = 8,

    /// <summary>The signature uses an algorithm this deployment does not verify.</summary>
    UnsupportedAlgorithm = 9,

    /// <summary>The signature could not be read.</summary>
    MalformedSignature = 10,

    /// <summary>The S/MIME body is encrypted or compressed, so the signature inside it was not reached.</summary>
    ContentNotOpened = 11,
}
