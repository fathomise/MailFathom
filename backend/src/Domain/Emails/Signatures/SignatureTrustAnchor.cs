// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Emails.Signatures;

/// <summary>Where the trust a signature's signer was established by comes from.</summary>
public enum SignatureTrustAnchor
{
    /// <summary>The certificate chained to a certificate authority in the operating system's trust store.</summary>
    SystemTrustStore = 0,

    /// <summary>The certificate chained to a certificate authority the operator configured.</summary>
    ConfiguredAuthority = 1,

    /// <summary>The OpenPGP signing key is one the operator configured.</summary>
    ConfiguredKey = 2,
}
