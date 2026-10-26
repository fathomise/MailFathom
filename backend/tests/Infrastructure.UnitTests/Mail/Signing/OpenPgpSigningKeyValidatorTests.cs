// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Mail.Signing;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Signing;

/// <summary>Covers which OpenPGP secret keys an account may sign its mail with, and what each refusal is called.</summary>
public sealed class OpenPgpSigningKeyValidatorTests
{
    private const string FromAddress = "sender@example.test";

    private const string KeyReference = "file:/etc/mailfathom/openpgp/signing.asc";

    private const string PassphraseReference = "systemd-credential:mailfathom-openpgp-passphrase";

    private static readonly DateTimeOffset Now = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FindUnusabilityAsync_UnprotectedKeyWhosePrimaryKeySigns_AcceptsTheKeyAndErasesTheMaterial()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        material.ProvisionText(KeyReference, SigningIdentityFixtures.OpenPgpSigner);

        // Act
        var failure = await ValidatorFor(material).FindUnusabilityAsync(KeyOf(), FromAddress, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(failure);
        Assert.All(
            material.IssuedMaterial,
            issued => Assert.Throws<ObjectDisposedException>(() => issued.RevealBytes().Length));
    }

    [Fact]
    public async Task FindUnusabilityAsync_ProtectedKeyWithItsPassphrase_AcceptsTheKey()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        material.ProvisionText(KeyReference, SigningIdentityFixtures.OpenPgpProtected);
        material.ProvisionText(PassphraseReference, SigningIdentityFixtures.OpenPgpPassphrase);

        // Act
        var failure = await ValidatorFor(material).FindUnusabilityAsync(
            KeyOf(PassphraseReference),
            FromAddress,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(failure);
    }

    [Fact]
    public async Task FindUnusabilityAsync_KeyThatSignsOnlyThroughItsSubkey_AcceptsTheKey()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        material.ProvisionText(KeyReference, SigningIdentityFixtures.OpenPgpSubkeySigner);

        // Act
        var failure = await ValidatorFor(material).FindUnusabilityAsync(KeyOf(), FromAddress, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(failure);
    }

    [Fact]
    public async Task FindUnusabilityAsync_ProtectedKeyWithNoPassphraseConfigured_RefusesTheKeyAsUnopened()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        material.ProvisionText(KeyReference, SigningIdentityFixtures.OpenPgpProtected);

        // Act
        var failure = await ValidatorFor(material).FindUnusabilityAsync(KeyOf(), FromAddress, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OpenPgpSigningKeyFailure.PassphraseMissing, failure);
    }

    [Fact]
    public async Task FindUnusabilityAsync_ProtectedKeyWithTheWrongPassphrase_RefusesTheKeyAsUnopened()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        material.ProvisionText(KeyReference, SigningIdentityFixtures.OpenPgpProtected);
        material.ProvisionText(PassphraseReference, "the-other-passphrase");

        // Act
        var failure = await ValidatorFor(material).FindUnusabilityAsync(
            KeyOf(PassphraseReference),
            FromAddress,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OpenPgpSigningKeyFailure.PassphraseIncorrect, failure);
    }

    [Fact]
    public async Task FindUnusabilityAsync_KeyReferenceThatResolvesNothing_RefusesTheKeyAsUnresolvable()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();

        // Act
        var failure = await ValidatorFor(material).FindUnusabilityAsync(KeyOf(), FromAddress, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OpenPgpSigningKeyFailure.SecretNotResolvable, failure);
    }

    [Theory]
    [InlineData("this is not a key")]
    [InlineData(SigningIdentityFixtures.OpenPgpPublicKey)]
    public async Task FindUnusabilityAsync_MaterialThatIsNoArmoredSecretKey_RefusesTheMaterial(string configuredMaterial)
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        material.ProvisionText(KeyReference, configuredMaterial);

        // Act
        var failure = await ValidatorFor(material).FindUnusabilityAsync(KeyOf(), FromAddress, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OpenPgpSigningKeyFailure.NotAnArmoredSecretKey, failure);
    }

    [Fact]
    public async Task FindUnusabilityAsync_ArmoredBlockThatDoesNotParse_RefusesTheMaterialAsUnreadable()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        material.ProvisionText(
            KeyReference,
            "-----BEGIN PGP PRIVATE KEY BLOCK-----\n\nbm90IGEga2V5\n-----END PGP PRIVATE KEY BLOCK-----\n");

        // Act
        var failure = await ValidatorFor(material).FindUnusabilityAsync(KeyOf(), FromAddress, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OpenPgpSigningKeyFailure.MaterialNotReadable, failure);
    }

    [Fact]
    public async Task FindUnusabilityAsync_BlockHoldingTwoKeys_RefusesTheMaterialAsAmbiguous()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        material.ProvisionText(KeyReference, SigningIdentityFixtures.OpenPgpTwoKeys);

        // Act
        var failure = await ValidatorFor(material).FindUnusabilityAsync(KeyOf(), FromAddress, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OpenPgpSigningKeyFailure.MaterialCarriesSeveralKeys, failure);
    }

    [Fact]
    public async Task FindUnusabilityAsync_KeyThatOnlyCertifiesAndEncrypts_RefusesTheKeyAsUnableToSign()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        material.ProvisionText(KeyReference, SigningIdentityFixtures.OpenPgpNoSigningKey);

        // Act
        var failure = await ValidatorFor(material).FindUnusabilityAsync(KeyOf(), FromAddress, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OpenPgpSigningKeyFailure.SigningKeyMissing, failure);
    }

    [Fact]
    public async Task FindUnusabilityAsync_KeyForAnotherAddress_RefusesTheKey()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        material.ProvisionText(KeyReference, SigningIdentityFixtures.OpenPgpOtherUserId);

        // Act
        var failure = await ValidatorFor(material).FindUnusabilityAsync(KeyOf(), FromAddress, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OpenPgpSigningKeyFailure.UserIdDoesNotMatchAddress, failure);
    }

    [Fact]
    public async Task FindUnusabilityAsync_KeyCarryingARevocation_RefusesTheKey()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        material.ProvisionText(KeyReference, SigningIdentityFixtures.OpenPgpRevoked);

        // Act
        var failure = await ValidatorFor(material).FindUnusabilityAsync(KeyOf(), FromAddress, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OpenPgpSigningKeyFailure.KeyRevoked, failure);
    }

    /// <summary>The key expired on 2026-03-01, so it is refused after that day and accepted before it.</summary>
    [Theory]
    [InlineData(2026, 2, null)]
    [InlineData(2026, 3, nameof(OpenPgpSigningKeyFailure.KeyExpired))]
    public async Task FindUnusabilityAsync_KeyWithAnExpiry_IsJudgedAgainstTheClock(int year, int month, string? expected)
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        material.ProvisionText(KeyReference, SigningIdentityFixtures.OpenPgpExpired);
        var validator = new OpenPgpSigningKeyValidator(
            material,
            new FakeTimeProvider(new DateTimeOffset(year, month, 15, 0, 0, 0, TimeSpan.Zero)));

        // Act
        var failure = await validator.FindUnusabilityAsync(KeyOf(), FromAddress, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expected is null ? null : Enum.Parse<OpenPgpSigningKeyFailure>(expected), failure);
    }

    private static OpenPgpSigningKeyValidator ValidatorFor(ProvisionedMaterialResolver material) =>
        new(material, new FakeTimeProvider(Now));

    private static ConfiguredSecret KeyOf(string? passphraseReference = null) => new()
    {
        Name = "openpgp-signing-key",
        SecretReference = KeyReference,
        Password = passphraseReference is null
            ? null
            : new ConfiguredSecret { Name = "openpgp-signing-passphrase", SecretReference = passphraseReference },
    };
}
