// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails.Signatures;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>The signature columns one email summary is built from, as PostgreSQL returns them.</summary>
/// <remarks>
/// A group of its own inside <see cref="StoredEmailSummaryRow" /> so the summary's projection names the signature once
/// rather than as twenty-five positional arguments among its others. The signer's key material is not among them: it is
/// kept for the features that will act on it, and no listing, read, or answer publishes it.
/// </remarks>
internal sealed record StoredEmailSignatureRow(
    EmailSignatureFormat? Format,
    SecureMimeContentType? SecureMimeType,
    EmailSignatureUnverifiableReason? UnverifiableReason,
    DateTimeOffset? SignedAt,
    SignatureCheckResult ContentCheck,
    SignatureCheckResult AuthorityCheck,
    SignatureTrustAnchor? TrustAnchor,
    string? ChainEnd,
    SignatureCheckResult ValidityCheck,
    EmailSignatureUnverifiableReason? ValidityFailure,
    SignatureCheckResult RevocationCheck,
    RevocationCheckMethod? RevocationMethod,
    DateTimeOffset? RevocationCheckedAt,
    DateTimeOffset? RevokedAt,
    SignatureCheckResult SignerCheck,
    string? SignerName,
    string? SignerAddress,
    string? SignerIssuer,
    DateTimeOffset? SignerValidFrom,
    DateTimeOffset? SignerValidUntil,
    string? SignerSerialNumber,
    string? SignerFingerprint,
    string? SignerKeyId,
    string? SignerPublicKeyAlgorithm,
    string? SignerDigestAlgorithm)
{
    /// <summary>Turns the returned columns back into the verification they were written from.</summary>
    /// <returns>The verification, without the signer's key material.</returns>
    /// <remarks>
    /// A signer is read back whenever a column identifying one holds a value, which is every verification that reached
    /// a certificate or named a key, and none of those that stopped before either.
    /// </remarks>
    public EmailSignature ToSignature() => new()
    {
        Format = this.Format,
        SecureMimeType = this.SecureMimeType,
        UnverifiableReason = this.UnverifiableReason,
        SignedAt = this.SignedAt,
        ContentCheck = this.ContentCheck,
        AuthorityCheck = this.AuthorityCheck,
        TrustAnchor = this.TrustAnchor,
        ChainEnd = this.ChainEnd,
        ValidityCheck = this.ValidityCheck,
        ValidityFailure = this.ValidityFailure,
        RevocationCheck = this.RevocationCheck,
        RevocationMethod = this.RevocationMethod,
        RevocationCheckedAt = this.RevocationCheckedAt,
        RevokedAt = this.RevokedAt,
        SignerCheck = this.SignerCheck,
        Signer = this.SignerFingerprint is null && this.SignerKeyId is null && this.SignerAddress is null && this.SignerName is null
            ? null
            : new EmailSigner
            {
                Name = this.SignerName,
                Address = this.SignerAddress,
                Issuer = this.SignerIssuer,
                ValidFrom = this.SignerValidFrom,
                ValidUntil = this.SignerValidUntil,
                SerialNumber = this.SignerSerialNumber,
                Fingerprint = this.SignerFingerprint,
                KeyId = this.SignerKeyId,
                PublicKeyAlgorithm = this.SignerPublicKeyAlgorithm,
                DigestAlgorithm = this.SignerDigestAlgorithm,
            },
    };
}
