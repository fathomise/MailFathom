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
/// </para>
/// </remarks>
internal static class OpenPgpSignatureVerifier
{
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
            return signature with
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
        }

        var contentCheck = VerifyContent(pgpSignature, signingKey, signedContent);
        if (contentCheck is null)
        {
            return signature with { UnverifiableReason = EmailSignatureUnverifiableReason.UnsupportedAlgorithm };
        }

        return JudgeKey(signature with { ContentCheck = contentCheck.Value }, pgpSignature, signingKey, ring, displayedSenderAddress);
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

            var factory = new PgpObjectFactory(decoded);
            var next = factory.NextPgpObject();
            if (next is PgpCompressedData compressed)
            {
                next = new PgpObjectFactory(compressed.GetDataStream()).NextPgpObject();
            }

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

    private static EmailSignature JudgeKey(
        EmailSignature signature,
        PgpSignature pgpSignature,
        PgpPublicKey signingKey,
        PgpPublicKeyRing ring,
        string? displayedSenderAddress)
    {
        var primaryKey = ring.GetPublicKey();
        var signedAt = pgpSignature.CreationTime;
        var expired = new[] { primaryKey, signingKey }.Any(key => !IsValidAt(key, signedAt));
        var revocation = new[] { primaryKey, signingKey }
            .SelectMany(key => key.GetSignaturesOfType(key.IsMasterKey ? PgpSignature.KeyRevocation : PgpSignature.SubkeyRevocation))
            .FirstOrDefault();

        var userIds = primaryKey.GetUserIds().ToList();
        var addresses = userIds
            .Select(userId => MailboxAddress.TryParse(userId, out var mailbox) ? mailbox.Address : null)
            .OfType<string>()
            .ToList();
        var matchingAddress = addresses.FirstOrDefault(address => string.Equals(address, displayedSenderAddress, StringComparison.OrdinalIgnoreCase));
        var validSeconds = primaryKey.GetValidSeconds();

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

    private static bool IsValidAt(PgpPublicKey key, DateTime instant)
    {
        var validSeconds = key.GetValidSeconds();

        return key.CreationTime <= instant && (validSeconds <= 0 || key.CreationTime.AddSeconds(validSeconds) > instant);
    }

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
