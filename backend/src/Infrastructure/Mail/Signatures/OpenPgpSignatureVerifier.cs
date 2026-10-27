// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails.Signatures;
using MimeKit;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Security;

namespace MailFathom.Infrastructure.Mail.Signatures;

/// <summary>Verifies a PGP/MIME signature against the OpenPGP keys the operator configured, and nothing else.</summary>
/// <remarks>
/// <para>
/// OpenPGP has no certificate authorities, so trust is exactly the configured set. Nothing is fetched from a key server,
/// from WKD, or from DANE, and a key a message carries about itself — an <c>Autocrypt</c> header, an attached key — is
/// never consulted: it proves nothing about who holds it. A signature by a key outside the set is not verifiable here,
/// and its content is not checked, because there is no key to check it with.
/// </para>
/// <para>
/// A configured key's validity and revocation are read off the key itself — its self-signature's expiry and any
/// revocation signature it carries — since an OpenPGP key publishes both about itself rather than through a responder.
/// Only what the key verifiably said about itself counts: a user ID or an expiry is read from a self-certification that
/// verifies against the primary key, and a subkey signs for the key only through a binding the primary key made that
/// allows signing.
/// </para>
/// </remarks>
internal static class OpenPgpSignatureVerifier
{
    private static readonly int[] SelfCertificationTypes =
    [
        PgpSignature.DefaultCertification,
        PgpSignature.NoCertification,
        PgpSignature.CasualCertification,
        PgpSignature.PositiveCertification,
    ];

    /// <summary>Verifies one detached OpenPGP signature.</summary>
    /// <param name="signedContent">The canonical signed content.</param>
    /// <param name="signatureBlock">The signature part's decoded octets, armored or binary.</param>
    /// <param name="trustedKeys">The configured keys that loaded.</param>
    /// <param name="displayedSenderAddress">The address the message's <c>From</c> header wrote.</param>
    /// <returns>The verification.</returns>
    public static EmailSignature Verify(
        ReadOnlyMemory<byte> signedContent,
        ReadOnlyMemory<byte> signatureBlock,
        PgpPublicKeyRingBundle trustedKeys,
        string? displayedSenderAddress)
    {
        ArgumentNullException.ThrowIfNull(trustedKeys);

        var signature = new EmailSignature { Format = EmailSignatureFormat.OpenPgpMime };

        if (ReadFirstSignature(signatureBlock) is not { } pgpSignature)
        {
            return signature with { UnverifiableReason = EmailSignatureUnverifiableReason.MalformedSignature };
        }

        signature = signature with { SignedAt = new DateTimeOffset(pgpSignature.CreationTime, TimeSpan.Zero) };

        var signingKey = trustedKeys.GetPublicKey(pgpSignature.KeyId);
        var ring = signingKey is null ? null : trustedKeys.GetPublicKeyRing(pgpSignature.KeyId);
        if (signingKey is null || ring is null)
        {
            return UnknownKey(signature, pgpSignature);
        }

        // A subkey is trusted only through a binding its primary key made and that allows signing. A ring copied from a
        // key server can carry a subkey somebody else appended, and the presence of its packet proves nothing about who
        // holds it.
        var binding = signingKey.IsMasterKey ? null : SigningBindingOf(ring.GetPublicKey(), signingKey);
        if (!signingKey.IsMasterKey && binding is null)
        {
            return UnknownKey(signature, pgpSignature);
        }

        // RFC 9580 § 9.5 forbids validating a recent signature over one of these: a chosen-prefix collision lets a
        // signature made over one text stand for another.
        if (pgpSignature.HashAlgorithm is HashAlgorithmTag.MD5 or HashAlgorithmTag.Sha1 or HashAlgorithmTag.RipeMD160)
        {
            return signature with
            {
                ContentCheck = SignatureCheckResult.NotChecked,
                UnverifiableReason = EmailSignatureUnverifiableReason.UnsupportedAlgorithm,
            };
        }

        var contentCheck = VerifyContent(pgpSignature, signingKey, signedContent);
        if (contentCheck is null)
        {
            return signature with
            {
                ContentCheck = SignatureCheckResult.NotChecked,
                UnverifiableReason = EmailSignatureUnverifiableReason.UnsupportedAlgorithm,
            };
        }

        return JudgeKey(signature with { ContentCheck = contentCheck.Value }, pgpSignature, signingKey, binding, ring, displayedSenderAddress);
    }

    /// <summary>Reads configured armored or binary public keys into one set, skipping nothing silently: a key that does not parse throws.</summary>
    /// <param name="material">One configured key, or several concatenated.</param>
    /// <returns>The keys the material holds.</returns>
    /// <exception cref="IOException">Thrown when the material is not an OpenPGP public key.</exception>
    /// <exception cref="PgpException">Thrown when the material is not an OpenPGP public key.</exception>
    public static IReadOnlyList<PgpPublicKeyRing> ReadPublicKeys(ReadOnlySpan<byte> material)
    {
        using var stream = new MemoryStream(material.ToArray());
        using var decoded = PgpUtilities.GetDecoderStream(stream);

        var rings = new PgpPublicKeyRingBundle(decoded).GetKeyRings().ToList();

        return rings.Count == 0 ? throw new PgpException("The material holds no OpenPGP public key.") : rings;
    }

    private static PgpSignature? ReadFirstSignature(ReadOnlyMemory<byte> signatureBlock)
    {
        try
        {
            using var stream = new MemoryStream(signatureBlock.ToArray());
            using var decoded = PgpUtilities.GetDecoderStream(stream);

            // A detached RFC 3156 signature is never compressed, and opening a compressed packet would expand whatever a
            // stranger packed into it, so a compressed block is read as no signature at all.
            var next = new PgpObjectFactory(decoded).NextPgpObject();

            return next is PgpSignatureList { Count: > 0 } signatures ? signatures[0] : null;
        }
        catch (Exception exception) when (IsUnreadable(exception))
        {
            return null;
        }
    }

    private static SignatureCheckResult? VerifyContent(PgpSignature signature, PgpPublicKey key, ReadOnlyMemory<byte> signedContent)
    {
        try
        {
            signature.InitVerify(key);
        }
        catch (Exception exception) when (IsUnreadable(exception))
        {
            return null;
        }

        try
        {
            signature.Update(signedContent.ToArray());

            return signature.Verify() ? SignatureCheckResult.Passed : SignatureCheckResult.Failed;
        }
        catch (Exception exception) when (IsUnreadable(exception))
        {
            return SignatureCheckResult.Failed;
        }
    }

    private static EmailSignature UnknownKey(EmailSignature signature, PgpSignature pgpSignature) =>
        signature with
        {
            ContentCheck = SignatureCheckResult.NotChecked,
            AuthorityCheck = SignatureCheckResult.Failed,
            UnverifiableReason = EmailSignatureUnverifiableReason.UnknownKey,
            Signer = new EmailSigner
            {
                KeyId = $"{pgpSignature.KeyId:X16}",
                PublicKeyAlgorithm = PublicKeyAlgorithmOf(pgpSignature.KeyAlgorithm, bitStrength: null),
                DigestAlgorithm = HashAlgorithmOf(pgpSignature.HashAlgorithm),
            },
        };

    /// <summary>Finds the newest subkey binding the primary key made that verifies and allows signing.</summary>
    private static PgpSignature? SigningBindingOf(PgpPublicKey primaryKey, PgpPublicKey subkey) =>
        subkey.GetSignaturesOfType(PgpSignature.SubkeyBinding)
            .Where(binding => (binding.GetHashedSubPackets()?.GetKeyFlags() & PgpKeyFlags.CanSign) != 0)
            .Where(binding => Verifies(binding, primaryKey, certify => certify.VerifyCertification(primaryKey, subkey)))
            .OrderByDescending(binding => binding.CreationTime)
            .FirstOrDefault();

    /// <summary>Reads the user IDs the primary key certified itself, each with the newest self-certification that verifies.</summary>
    /// <remarks>
    /// A user ID nobody but its own key could have bound is the only one that says whose key this is; one appended
    /// without a verifying self-certification would let anybody give a trusted key their own address.
    /// </remarks>
    private static IReadOnlyList<(string UserId, PgpSignature Certification)> SelfCertifiedUserIdsOf(PgpPublicKey primaryKey) =>
    [
        .. primaryKey.GetUserIds()
            .Select(userId => (
                UserId: userId,
                Certification: (primaryKey.GetSignaturesForId(userId) ?? [])
                    .Where(certification => SelfCertificationTypes.Contains(certification.SignatureType))
                    .Where(certification => Verifies(certification, primaryKey, certify => certify.VerifyCertification(userId, primaryKey)))
                    .OrderByDescending(certification => certification.CreationTime)
                    .FirstOrDefault()))
            .Where(candidate => candidate.Certification is not null)
            .Select(candidate => (candidate.UserId, candidate.Certification!)),
    ];

    private static bool Verifies(PgpSignature signature, PgpPublicKey signer, Func<PgpSignature, bool> verify)
    {
        try
        {
            signature.InitVerify(signer);

            return verify(signature);
        }
        catch (Exception exception) when (IsUnreadable(exception))
        {
            return false;
        }
    }

    private static EmailSignature JudgeKey(
        EmailSignature signature,
        PgpSignature pgpSignature,
        PgpPublicKey signingKey,
        PgpSignature? binding,
        PgpPublicKeyRing ring,
        string? displayedSenderAddress)
    {
        var primaryKey = ring.GetPublicKey();
        var signedAt = pgpSignature.CreationTime;
        var certifiedUserIds = SelfCertifiedUserIdsOf(primaryKey);

        // Expiry is read only from a self-certification or a binding that verified, never from a packet merely present.
        var validSeconds = certifiedUserIds
            .Select(candidate => candidate.Certification)
            .OrderByDescending(certification => certification.CreationTime)
            .Select(ExpirySecondsOf)
            .FirstOrDefault();
        var expired = !IsValidAt(primaryKey.CreationTime, validSeconds, signedAt)
            || (binding is not null && !IsValidAt(signingKey.CreationTime, ExpirySecondsOf(binding), signedAt));
        var revocation = new[] { primaryKey, signingKey }
            .SelectMany(key => key.GetSignaturesOfType(key.IsMasterKey ? PgpSignature.KeyRevocation : PgpSignature.SubkeyRevocation))
            .FirstOrDefault();

        var userIds = certifiedUserIds.Select(candidate => candidate.UserId).ToList();
        var addresses = userIds
            .Select(userId => MailboxAddress.TryParse(userId, out var mailbox) ? mailbox.Address : null)
            .OfType<string>()
            .ToList();
        var matchingAddress = addresses.FirstOrDefault(address => string.Equals(address, displayedSenderAddress, StringComparison.OrdinalIgnoreCase));

        signature = signature with
        {
            AuthorityCheck = SignatureCheckResult.Passed,
            TrustAnchor = SignatureTrustAnchor.ConfiguredKey,
            ValidityCheck = expired ? SignatureCheckResult.Failed : SignatureCheckResult.Passed,
            ValidityFailure = expired ? EmailSignatureUnverifiableReason.ExpiredKey : null,
            RevocationCheck = revocation is null ? SignatureCheckResult.Passed : SignatureCheckResult.Failed,
            RevocationMethod = RevocationCheckMethod.KeyRevocationSignature,
            RevokedAt = revocation is null ? null : new DateTimeOffset(revocation.CreationTime, TimeSpan.Zero),
            SignerCheck = matchingAddress is null ? SignatureCheckResult.Failed : SignatureCheckResult.Passed,
            Signer = new EmailSigner
            {
                Name = userIds.FirstOrDefault(),
                Address = matchingAddress ?? addresses.FirstOrDefault(),
                ValidFrom = new DateTimeOffset(primaryKey.CreationTime, TimeSpan.Zero),
                ValidUntil = validSeconds > 0 ? new DateTimeOffset(primaryKey.CreationTime, TimeSpan.Zero).AddSeconds(validSeconds) : null,
                Fingerprint = Convert.ToHexString(signingKey.GetFingerprint()),
                KeyId = $"{signingKey.KeyId:X16}",
                PublicKeyAlgorithm = PublicKeyAlgorithmOf(signingKey.Algorithm, signingKey.BitStrength),
                DigestAlgorithm = HashAlgorithmOf(pgpSignature.HashAlgorithm),
                KeyMaterial = ring.GetEncoded(),
            },
        };

        return signature.ContentCheck == SignatureCheckResult.Failed
            ? signature
            : signature with
            {
                UnverifiableReason = expired ? EmailSignatureUnverifiableReason.ExpiredKey
                    : revocation is not null ? EmailSignatureUnverifiableReason.RevokedKey
                    : null,
            };
    }

    private static long ExpirySecondsOf(PgpSignature certification) =>
        certification.GetHashedSubPackets()?.GetKeyExpirationTime() ?? 0;

    private static bool IsValidAt(DateTime createdAt, long validSeconds, DateTime instant) =>
        createdAt <= instant && (validSeconds <= 0 || createdAt.AddSeconds(validSeconds) > instant);

    private static string PublicKeyAlgorithmOf(PublicKeyAlgorithmTag algorithm, int? bitStrength) => algorithm switch
    {
        PublicKeyAlgorithmTag.RsaGeneral or PublicKeyAlgorithmTag.RsaSign => bitStrength is { } bits ? $"RSA {bits}" : "RSA",
        PublicKeyAlgorithmTag.Dsa => "DSA",
        PublicKeyAlgorithmTag.ECDsa => "ECDSA",
        PublicKeyAlgorithmTag.EdDsa_Legacy or PublicKeyAlgorithmTag.Ed25519 => "Ed25519",
        PublicKeyAlgorithmTag.Ed448 => "Ed448",
        _ => algorithm.ToString(),
    };

    private static string HashAlgorithmOf(HashAlgorithmTag hash) => hash switch
    {
        HashAlgorithmTag.Sha1 => "SHA-1",
        HashAlgorithmTag.Sha224 => "SHA-224",
        HashAlgorithmTag.Sha256 => "SHA-256",
        HashAlgorithmTag.Sha384 => "SHA-384",
        HashAlgorithmTag.Sha512 => "SHA-512",
        _ => hash.ToString(),
    };

    /// <summary>Recognizes what a signature or key a stranger wrote can throw while it is read.</summary>
    private static bool IsUnreadable(Exception exception) =>
        exception is PgpException or IOException or ArgumentException or InvalidOperationException or FormatException
            or InvalidCastException or GeneralSecurityException or SecurityUtilityException or CryptoException;
}
