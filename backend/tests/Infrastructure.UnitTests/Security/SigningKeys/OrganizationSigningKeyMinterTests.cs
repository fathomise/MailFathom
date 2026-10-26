// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Security.Cryptography;
using MailFathom.Common;
using MailFathom.Infrastructure.DataEncryption;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Security.SigningKeys;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using Org.BouncyCastle.Crypto.Parameters;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Security.SigningKeys;

/// <summary>Covers what a minted organization signing key is: an Ed25519 pair whose private half leaves only sealed, and only for its own key.</summary>
public sealed class OrganizationSigningKeyMinterTests
{
    private const string RingKeyId = "2026-10";

    private static readonly Guid KeyId = new("0199a0c0-0000-7000-8000-0000000000a1");

    private static readonly Guid OtherKeyId = new("0199a0c0-0000-7000-8000-0000000000a2");

    [Fact]
    public async Task MintAsync_AConfiguredRing_SealsAPrivateHalfThatOpensToTheKeyItsPublicHalfBelongsTo()
    {
        // Arrange
        var encryptor = CreateEncryptor(configured: true, out var ring);
        var minter = new OrganizationSigningKeyMinter(ring, encryptor);

        // Act
        var minted = await minter.MintAsync(KeyId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(KeyId, minted.KeyId);
        Assert.Equal(RingKeyId, minted.SealedPrivateKey.KeyId);
        Assert.Equal(Ed25519PublicKeyParameters.KeySize, minted.PublicKey.Length);

        var opened = await encryptor.OpenAsync(
            OrganizationSigningKeyMinter.BindingFor(KeyId),
            minted.SealedPrivateKey,
            TestContext.Current.CancellationToken);
        var privateKey = new Ed25519PrivateKeyParameters(opened);
        Assert.Equal(minted.PublicKey, privateKey.GeneratePublicKey().GetEncoded());
    }

    /// <summary>The seal is bound to the key's identifier, so a sealed half moved onto another key's row never signs as that key.</summary>
    [Fact]
    public async Task MintAsync_APrivateHalfMovedOntoAnotherKey_DoesNotOpen()
    {
        // Arrange
        var encryptor = CreateEncryptor(configured: true, out var ring);
        var minted = await new OrganizationSigningKeyMinter(ring, encryptor).MintAsync(KeyId, TestContext.Current.CancellationToken);

        // Act
        var opening = async () => await encryptor.OpenAsync(
            OrganizationSigningKeyMinter.BindingFor(OtherKeyId),
            minted.SealedPrivateKey,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAnyAsync<CryptographicException>(opening);
    }

    [Fact]
    public async Task MintAsync_TwoKeys_AreDrawnIndependently()
    {
        // Arrange
        var encryptor = CreateEncryptor(configured: true, out var ring);
        var minter = new OrganizationSigningKeyMinter(ring, encryptor);

        // Act
        var first = await minter.MintAsync(KeyId, TestContext.Current.CancellationToken);
        var second = await minter.MintAsync(OtherKeyId, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(first.PublicKey, second.PublicKey);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CanMint_ARing_SaysWhetherItConfiguresAnyKey(bool configured)
    {
        // Arrange
        var encryptor = CreateEncryptor(configured, out var ring);

        // Act
        var canMint = new OrganizationSigningKeyMinter(ring, encryptor).CanMint;

        // Assert
        Assert.Equal(configured, canMint);
    }

    private static FieldEncryptor CreateEncryptor(bool configured, out DataEncryptionKeyRing ring)
    {
        var resolver = new ProvisionedMaterialResolver();
        resolver.ProvisionText("plaintext:active", Convert.ToBase64String([.. Enumerable.Repeat((byte)0x11, AesGcmEnvelope.KeySizeInBytes)]));

        var settings = new DataEncryptionKeyRingSettings(
            RingKeyId,
            configured
                ? [new DataEncryptionKeyReference(RingKeyId, new ConfiguredSecret { Name = "data-key-active", SecretReference = "plaintext:active" })]
                : []);
        ring = new DataEncryptionKeyRing(() => settings, resolver);

        return new FieldEncryptor(ring);
    }
}
