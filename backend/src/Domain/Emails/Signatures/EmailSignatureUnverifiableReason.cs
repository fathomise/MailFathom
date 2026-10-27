// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Emails.Signatures;

/// <summary>Why a signature is not verifiable here — the first check, in the order they run, that did not pass.</summary>
public enum EmailSignatureUnverifiableReason
{
    /// <summary>The S/MIME signature carries no certificate for its signer.</summary>
    NoCertificate = 0,

    /// <summary>The signer's certificate does not chain to a certificate authority this deployment trusts.</summary>
    UntrustedRoot = 1,

    /// <summary>A certificate in the chain was outside its validity period at the signing time.</summary>
    OutsideValidityPeriod = 2,

    /// <summary>The signer's certificate is not meant for signing email: no <c>emailProtection</c> usage, or no signing key usage.</summary>
    WrongKeyUsage = 3,

    /// <summary>The signer's certificate is revoked, or so is a certificate above it in the chain.</summary>
    Revoked = 4,

    /// <summary>
    /// Whether the signer's certificate, or a certificate above it in the chain that publishes a responder or a list, is
    /// revoked could not be established: no responder answered, or the signer publishes none.
    /// </summary>
    RevocationNotCheckable = 5,

    /// <summary>The OpenPGP signing key is not one this deployment's operator configured.</summary>
    UnknownKey = 6,

    /// <summary>The OpenPGP signing key had expired, or did not yet exist, at the signing time.</summary>
    ExpiredKey = 7,

    /// <summary>The OpenPGP signing key carries a revocation.</summary>
    RevokedKey = 8,

    /// <summary>The signature uses an algorithm this deployment cannot verify.</summary>
    UnsupportedAlgorithm = 9,

    /// <summary>The signature block, or a certificate or key inside it, does not parse.</summary>
    MalformedSignature = 10,

    /// <summary>The S/MIME part wraps its content in encryption or compression, which is not opened here.</summary>
    ContentNotOpened = 11,
}
