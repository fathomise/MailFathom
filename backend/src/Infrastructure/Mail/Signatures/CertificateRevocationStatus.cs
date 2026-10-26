// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Infrastructure.Mail.Signatures;

/// <summary>Whether a certificate is revoked, or whether that could not be established.</summary>
internal enum CertificateRevocationStatus
{
    /// <summary>A responder or a list answered, and the certificate is not revoked.</summary>
    Good = 0,

    /// <summary>A responder or a list answered that the certificate is revoked.</summary>
    Revoked = 1,

    /// <summary>No responder or list gave a usable answer, or the certificate names none.</summary>
    Unavailable = 2,
}
