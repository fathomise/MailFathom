// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Secrets.References;
using MailFathom.Infrastructure.Secrets.Resolution;
using MimeKit;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;

namespace MailFathom.Infrastructure.Mail.Signing;

/// <summary>Proves that a configured OpenPGP secret key could sign this account's mail as PGP/MIME, or names why it could not.</summary>
/// <remarks>
/// <para>
/// The checks are the ones a recipient's client makes of a signature, moved to the moment the configuration is read:
/// a revoked or expired key, an encryption-only one, the key of another address, or a passphrase that does not open it
/// is refused there rather than discovered at the first send.
/// </para>
/// <para>
/// The key is the operator's own, so its signatures are read as they are stated rather than verified: a revocation or
/// an expiry present in the material is honored, which is all an operator provisioning their own key needs, and
/// nothing here decides whether somebody else's key is trustworthy.
/// </para>
/// <para>
/// Nothing is kept. The material and the passphrase are revealed into pinned buffers erased before this returns, every
/// private key the passphrase opened is dropped at once, and the answer is a failure identity carrying no material.
/// </para>
/// </remarks>
public sealed class OpenPgpSigningKeyValidator
{
    private static readonly int[] UserIdCertificationTypes =
    [
        PgpSignature.PositiveCertification,
        PgpSignature.CasualCertification,
        PgpSignature.NoCertification,
        PgpSignature.DefaultCertification,
    ];

    private static readonly int[] PrimaryKeySelfSignatureTypes = [.. UserIdCertificationTypes, PgpSignature.DirectKey];

    private static readonly int[] SubkeySelfSignatureTypes = [PgpSignature.SubkeyBinding];

    private readonly ISecretReferenceResolver secretReferenceResolver;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new validator.</summary>
    /// <param name="secretReferenceResolver">The resolver that turns a configured reference into material.</param>
    /// <param name="timeProvider">The clock a key's expiry is read against.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public OpenPgpSigningKeyValidator(ISecretReferenceResolver secretReferenceResolver, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(secretReferenceResolver);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.secretReferenceResolver = secretReferenceResolver;
        this.timeProvider = timeProvider;
    }

    /// <summary>Finds the first reason the configured secret key could not sign mail sent from an address.</summary>
    /// <param name="configuredKey">The block naming the ASCII-armored secret key, whose nested password block supplies its passphrase.</param>
    /// <param name="fromAddress">The address the account's mail is written from.</param>
    /// <param name="cancellationToken">Cancels the material and passphrase retrieval.</param>
    /// <returns>The failure, or <see langword="null" /> when the key could sign as <paramref name="fromAddress" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuredKey" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="fromAddress" /> is <see langword="null" />, empty, or white space.</exception>
    public async Task<OpenPgpSigningKeyFailure?> FindUnusabilityAsync(
        ConfiguredSecret configuredKey,
        string fromAddress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuredKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(fromAddress);

        var keyResult = await this.secretReferenceResolver.ResolveAsync(configuredKey.SecretReference, cancellationToken);

        if (keyResult.Secret is not { } keyMaterial)
        {
            return OpenPgpSigningKeyFailure.SecretNotResolvable;
        }

        using (keyMaterial)
        {
            ResolvedSecret? passphrase = null;

            if (configuredKey.Password is { SecretReference: var passphraseReference }
                && !string.IsNullOrWhiteSpace(passphraseReference))
            {
                var passphraseResult = await this.secretReferenceResolver.ResolveAsync(passphraseReference, cancellationToken);

                if (passphraseResult.Secret is null)
                {
                    return OpenPgpSigningKeyFailure.SecretNotResolvable;
                }

                passphrase = passphraseResult.Secret;
            }

            using (passphrase)
            {
                if (ReadKeyRing(keyMaterial, out var failure) is not { } keyRing)
                {
                    return failure;
                }

                return this.FindUnsuitability(keyRing, fromAddress.Trim(), passphrase);
            }
        }
    }

    /// <summary>Parses the material into the one secret key ring it must hold.</summary>
    /// <remarks>The bytes are copied into a pinned buffer for the parser's stream and erased before this returns.</remarks>
    private static PgpSecretKeyRing? ReadKeyRing(ResolvedSecret keyMaterial, out OpenPgpSigningKeyFailure? failure)
    {
        var material = keyMaterial.RevealBytes();

        if (!material.TrimStart(" \t\r\n"u8).StartsWith("-----BEGIN PGP PRIVATE KEY BLOCK-----"u8))
        {
            failure = OpenPgpSigningKeyFailure.NotAnArmoredSecretKey;

            return null;
        }

        // The armored stream ends at the first block's footer, so a second block concatenated after it would never be
        // parsed and the material would read as the first key alone.
        if (material.Count("-----BEGIN PGP "u8) > 1)
        {
            failure = OpenPgpSigningKeyFailure.MaterialCarriesSeveralKeys;

            return null;
        }

        var buffer = GC.AllocateArray<byte>(material.Length, pinned: true);

        try
        {
            material.CopyTo(buffer);

            PgpSecretKeyRing[] keyRings;
            try
            {
                using var armored = new ArmoredInputStream(new MemoryStream(buffer, writable: false));
                keyRings = [.. new PgpSecretKeyRingBundle(armored).GetKeyRings()];
            }
            catch (Exception parseFailure) when (parseFailure is IOException or PgpException or ArgumentException or InvalidOperationException)
            {
                failure = OpenPgpSigningKeyFailure.MaterialNotReadable;

                return null;
            }

            failure = keyRings.Length switch
            {
                0 => OpenPgpSigningKeyFailure.MaterialNotReadable,
                > 1 => OpenPgpSigningKeyFailure.MaterialCarriesSeveralKeys,
                _ => null,
            };

            return failure is null ? keyRings[0] : null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    /// <summary>Finds the first reason a parsed key cannot sign as an address, in the order an operator would fix them.</summary>
    private OpenPgpSigningKeyFailure? FindUnsuitability(
        PgpSecretKeyRing keyRing,
        string fromAddress,
        ResolvedSecret? passphrase)
    {
        var now = this.timeProvider.GetUtcNow().UtcDateTime;
        var primaryKey = keyRing.GetSecretKey().PublicKey;

        if (primaryKey.HasRevocation())
        {
            return OpenPgpSigningKeyFailure.KeyRevoked;
        }

        if (HasExpired(primaryKey, now))
        {
            return OpenPgpSigningKeyFailure.KeyExpired;
        }

        if (!primaryKey.GetUserIds().Any(userId => NamesAddress(userId, fromAddress) && IsCurrentlyCertified(primaryKey, userId)))
        {
            return OpenPgpSigningKeyFailure.UserIdDoesNotMatchAddress;
        }

        PgpSecretKey[] signingKeys = [.. keyRing.GetSecretKeys().Where(CanSign)];

        if (signingKeys.Length == 0)
        {
            return OpenPgpSigningKeyFailure.SigningKeyMissing;
        }

        signingKeys = [.. signingKeys.Where(static key => !key.PublicKey.HasRevocation())];

        if (signingKeys.Length == 0)
        {
            return OpenPgpSigningKeyFailure.KeyRevoked;
        }

        signingKeys = [.. signingKeys.Where(key => !HasExpired(key.PublicKey, now))];

        if (signingKeys.Length == 0)
        {
            return OpenPgpSigningKeyFailure.KeyExpired;
        }

        return signingKeys.Select(key => FindPassphraseFailure(key, passphrase)).FirstOrDefault(static failure => failure is not null);
    }

    /// <summary>Decides whether a key could make a signature: an algorithm that signs, a secret part present, and key flags that permit it where they are stated.</summary>
    /// <remarks>
    /// An absent key-flags subpacket leaves the algorithm to decide, which is what RFC 9580 § 5.2.3.29 says an
    /// implementation does with a key whose self-signatures state none. A subkey whose secret part is a stub, as an
    /// export of a key kept on a smart card leaves behind, has nothing to sign with however capable its algorithm is.
    /// </remarks>
    private static bool CanSign(PgpSecretKey key)
    {
        if (!key.IsSigningKey || key.IsPrivateKeyEmpty)
        {
            return false;
        }

        var selfSignatureTypes = key.IsMasterKey ? PrimaryKeySelfSignatureTypes : SubkeySelfSignatureTypes;
        int[] statedFlags =
        [
            .. selfSignatureTypes
                .SelectMany(type => key.PublicKey.GetSignaturesOfType(type))
                .Select(static signature => signature.GetHashedSubPackets())
                .Where(static subpackets => subpackets is not null && subpackets.HasSubpacket(SignatureSubpacketTag.KeyFlags))
                .Select(static subpackets => subpackets.GetKeyFlags()),
        ];

        return statedFlags.Length == 0
            || statedFlags.Any(static flags => (flags & PgpKeyFlags.CanSign) != 0);
    }

    private static bool HasExpired(PgpPublicKey key, DateTime now)
    {
        var validSeconds = key.GetValidSeconds();

        return validSeconds > 0 && now >= key.CreationTime.AddSeconds(validSeconds);
    }

    /// <summary>Decides whether the primary key still certifies a user ID: a self-certification newer than any self-revocation of it.</summary>
    /// <remarks>
    /// A recipient attributes a signature to a user ID only while the key binds it, so one the key never certified, or
    /// whose latest certification a later revocation withdrew, names nobody. A certification issued after a revocation
    /// binds the user ID again, which is how an implementation reinstates one.
    /// </remarks>
    private static bool IsCurrentlyCertified(PgpPublicKey primaryKey, string userId)
    {
        PgpSignature[] selfSignatures =
        [
            .. primaryKey.GetSignaturesForId(userId).Where(signature => signature.KeyId == primaryKey.KeyId),
        ];
        var latestCertification = selfSignatures
            .Where(static signature => UserIdCertificationTypes.Contains(signature.SignatureType))
            .Select(static signature => (DateTime?)signature.CreationTime)
            .Max();
        var latestRevocation = selfSignatures
            .Where(static signature => signature.SignatureType == PgpSignature.CertificationRevocation)
            .Select(static signature => (DateTime?)signature.CreationTime)
            .Max();

        return latestCertification is { } certifiedAt && (latestRevocation is not { } revokedAt || certifiedAt > revokedAt);
    }

    /// <summary>Matches a user ID such as <c>Name &lt;address&gt;</c>, or a bare address, against the sending address.</summary>
    private static bool NamesAddress(string userId, string fromAddress) =>
        MailboxAddress.TryParse(userId, out var mailbox)
        && string.Equals(mailbox.Address, fromAddress, StringComparison.OrdinalIgnoreCase);

    /// <summary>Opens the key with the configured passphrase, revealed into a buffer erased before this returns.</summary>
    /// <remarks>
    /// The passphrase is encoded as UTF-8, which is what GnuPG and every current implementation encode one as, so a
    /// passphrase outside ASCII opens the key it opens there. An unprotected key needs no passphrase and ignores one.
    /// </remarks>
    private static OpenPgpSigningKeyFailure? FindPassphraseFailure(PgpSecretKey key, ResolvedSecret? passphrase)
    {
        if (key.KeyEncryptionAlgorithm == SymmetricKeyAlgorithmTag.Null)
        {
            return null;
        }

        if (passphrase is null)
        {
            return OpenPgpSigningKeyFailure.PassphraseMissing;
        }

        var passphraseBuffer = GC.AllocateArray<char>(passphrase.TextLength, pinned: true);

        try
        {
            passphrase.RevealTextInto(passphraseBuffer);
            _ = key.ExtractPrivateKeyUtf8(passphraseBuffer);

            return null;
        }
        catch (PgpException)
        {
            return OpenPgpSigningKeyFailure.PassphraseIncorrect;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(passphraseBuffer.AsSpan()));
        }
    }
}
