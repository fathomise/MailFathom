// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails.Signatures;

namespace MailFathom.Infrastructure.Mail.Signatures;

/// <summary>What establishing one certificate's revocation status found.</summary>
/// <param name="Status">Whether the certificate is revoked, or whether that could not be established.</param>
/// <param name="Method">How the answer was obtained, absent where none was.</param>
/// <param name="CheckedAt">When the answer was obtained, absent where none was.</param>
/// <param name="RevokedAt">When the certificate was revoked, where the answer said it was.</param>
internal readonly record struct CertificateRevocationAnswer(
    CertificateRevocationStatus Status,
    RevocationCheckMethod? Method,
    DateTimeOffset? CheckedAt,
    DateTimeOffset? RevokedAt)
{
    /// <summary>Gets the answer of a check that could not complete.</summary>
    public static CertificateRevocationAnswer Unavailable { get; } =
        new(CertificateRevocationStatus.Unavailable, Method: null, CheckedAt: null, RevokedAt: null);
}
