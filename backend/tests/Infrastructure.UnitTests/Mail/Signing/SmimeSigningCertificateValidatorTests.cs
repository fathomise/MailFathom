// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Mail.Signing;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Signing;

/// <summary>Covers which S/MIME certificates an account may sign its mail with, and what each refusal is called.</summary>
public sealed class SmimeSigningCertificateValidatorTests
{
    private const string FromAddress = "sender@example.test";

    private const string BundleReference = "file:/etc/mailfathom/smime/signing.p12";

    private const string ChainReference = "file:/etc/mailfathom/smime/signing.pem";

    private const string PrivateKeyReference = "file:/etc/mailfathom/smime/signing.key";

    private const string PasswordReference = "systemd-credential:mailfathom-smime-password";

    private static readonly DateTimeOffset Now = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FindUnusabilityAsync_PemChainBesideAnUnencryptedKey_AcceptsTheCertificate()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        ProvidePem(material, SigningIdentityFixtures.SignerCertificate + "\n" + SigningIdentityFixtures.Authority, SigningIdentityFixtures.SignerPrivateKey);

        // Act
        var failure = await ValidatorFor(material).FindUnusabilityAsync(PemOf(), FromAddress, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(failure);
    }

    [Fact]
    public async Task FindUnusabilityAsync_EncryptedPemKeyWithItsPassword_AcceptsTheCertificate()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        ProvidePem(material, SigningIdentityFixtures.SignerCertificate, SigningIdentityFixtures.SignerEncryptedPrivateKey);
        material.ProvisionText(PasswordReference, SigningIdentityFixtures.SignerPrivateKeyPassword);

        // Act
        var failure = await ValidatorFor(material).FindUnusabilityAsync(
            PemOf(privateKeyPasswordReference: PasswordReference),
            FromAddress,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(failure);
    }

    [Fact]
    public async Task FindUnusabilityAsync_ProtectedPkcs12BundleWithItsPassword_AcceptsTheCertificateAndErasesEveryMaterial()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        material.Provision(BundleReference, SigningIdentityFixtures.SignerBundle);
        material.ProvisionText(PasswordReference, SigningIdentityFixtures.SignerBundlePassword);

        // Act
        var failure = await ValidatorFor(material).FindUnusabilityAsync(
            BundleOf(PasswordReference),
            FromAddress,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(failure);
        Assert.Equal(2, material.IssuedMaterial.Count);
        Assert.All(
            material.IssuedMaterial,
            issued => Assert.Throws<ObjectDisposedException>(() => issued.RevealBytes().Length));
    }

    /// <summary>RFC 8550 asks a receiving agent to recognize the legacy subject attribute as well as an alternative name, so a certificate naming the address only there signs as it.</summary>
    [Fact]
    public async Task FindUnusabilityAsync_AddressNamedOnlyInTheSubject_AcceptsTheCertificate()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        ProvidePem(material, SigningIdentityFixtures.SubjectEmailCertificate, SigningIdentityFixtures.SubjectEmailPrivateKey);

        // Act
        var failure = await ValidatorFor(material).FindUnusabilityAsync(PemOf(), FromAddress, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(failure);
    }

    /// <summary>RFC 8550 lets a signing certificate assert <c>nonRepudiation</c> instead of <c>digitalSignature</c>.</summary>
    [Fact]
    public async Task FindUnusabilityAsync_KeyUsagePermittingNonRepudiationAlone_AcceptsTheCertificate()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        ProvidePem(material, SigningIdentityFixtures.NonRepudiationOnlyCertificate, SigningIdentityFixtures.NonRepudiationOnlyPrivateKey);

        // Act
        var failure = await ValidatorFor(material).FindUnusabilityAsync(PemOf(), FromAddress, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(failure);
    }

    [Fact]
    public async Task FindUnusabilityAsync_SendingAddressInAnotherCase_AcceptsTheCertificate()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        ProvidePem(material, SigningIdentityFixtures.SignerCertificate, SigningIdentityFixtures.SignerPrivateKey);

        // Act
        var failure = await ValidatorFor(material).FindUnusabilityAsync(
            PemOf(),
            "Sender@Example.Test",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(failure);
    }

    [Fact]
    public async Task FindUnusabilityAsync_BundleHoldingNoPrivateKey_RefusesTheCertificateAsKeyless()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        material.Provision(BundleReference, SigningIdentityFixtures.SignerBundleWithoutKey);

        // Act
        var failure = await ValidatorFor(material).FindUnusabilityAsync(BundleOf(), FromAddress, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.PrivateKeyMissing, failure);
    }

    [Fact]
    public async Task FindUnusabilityAsync_BundleWithTheWrongPassword_RefusesTheMaterialAsUnopened()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        material.Provision(BundleReference, SigningIdentityFixtures.SignerBundle);
        material.ProvisionText(PasswordReference, "the-other-password");

        // Act
        var failure = await ValidatorFor(material).FindUnusabilityAsync(
            BundleOf(PasswordReference),
            FromAddress,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.BundlePasswordIncorrect, failure);
    }

    [Fact]
    public async Task FindUnusabilityAsync_ChainThatIsNoCertificate_RefusesTheMaterialAsUnreadable()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        ProvidePem(material, "this is not a certificate", SigningIdentityFixtures.SignerPrivateKey);

        // Act
        var failure = await ValidatorFor(material).FindUnusabilityAsync(PemOf(), FromAddress, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.EncodingNotRecognized, failure);
    }

    [Fact]
    public async Task FindUnusabilityAsync_PrivateKeyOfAnotherCertificate_RefusesThePairing()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        ProvidePem(material, SigningIdentityFixtures.SignerCertificate, SigningIdentityFixtures.OtherAddressPrivateKey);

        // Act
        var failure = await ValidatorFor(material).FindUnusabilityAsync(PemOf(), FromAddress, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.PrivateKeyDoesNotMatchCertificate, failure);
    }

    /// <summary>The certificates supplied beside the signer travel with every signature, so they are held to the rules a chain is.</summary>
    [Fact]
    public async Task FindUnusabilityAsync_ChainCarryingAnotherEndEntityCertificate_RefusesTheChain()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        ProvidePem(
            material,
            SigningIdentityFixtures.SignerCertificate + "\n" + SigningIdentityFixtures.OtherAddressCertificate,
            SigningIdentityFixtures.SignerPrivateKey);

        // Act
        var failure = await ValidatorFor(material).FindUnusabilityAsync(PemOf(), FromAddress, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.ChainCarriesNonAuthorityCertificate, failure);
    }

    [Fact]
    public async Task FindUnusabilityAsync_KeyUsagePermittingNoSignature_RefusesTheCertificate()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        ProvidePem(material, SigningIdentityFixtures.KeyAgreementOnlyCertificate, SigningIdentityFixtures.KeyAgreementOnlyPrivateKey);

        // Act
        var failure = await ValidatorFor(material).FindUnusabilityAsync(PemOf(), FromAddress, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.DigitalSignatureNotPermitted, failure);
    }

    [Fact]
    public async Task FindUnusabilityAsync_ExtendedKeyUsageExcludingEmailProtection_RefusesTheCertificate()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        ProvidePem(material, SigningIdentityFixtures.ServerAuthenticationOnlyCertificate, SigningIdentityFixtures.ServerAuthenticationOnlyPrivateKey);

        // Act
        var failure = await ValidatorFor(material).FindUnusabilityAsync(PemOf(), FromAddress, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.EmailProtectionNotPermitted, failure);
    }

    [Fact]
    public async Task FindUnusabilityAsync_CertificateForAnotherAddress_RefusesTheCertificate()
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        ProvidePem(material, SigningIdentityFixtures.OtherAddressCertificate, SigningIdentityFixtures.OtherAddressPrivateKey);

        // Act
        var failure = await ValidatorFor(material).FindUnusabilityAsync(PemOf(), FromAddress, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CertificateMaterialFailure.AddressNotCoveredByCertificate, failure);
    }

    /// <summary>The certificate is valid from 2026-01-01 to 2036-01-01, so a clock on either side of that reads it as outside its period.</summary>
    [Theory]
    [InlineData(2025, nameof(CertificateMaterialFailure.CertificateNotYetValid))]
    [InlineData(2036, nameof(CertificateMaterialFailure.CertificateExpired))]
    public async Task FindUnusabilityAsync_ClockOutsideTheValidityPeriod_RefusesTheCertificate(int year, string expected)
    {
        // Arrange
        var material = new ProvisionedMaterialResolver();
        ProvidePem(material, SigningIdentityFixtures.SignerCertificate, SigningIdentityFixtures.SignerPrivateKey);
        var validator = new SmimeSigningCertificateValidator(
            material,
            new FakeTimeProvider(new DateTimeOffset(year, 6, 1, 0, 0, 0, TimeSpan.Zero)));

        // Act
        var failure = await validator.FindUnusabilityAsync(PemOf(), FromAddress, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Enum.Parse<CertificateMaterialFailure>(expected), failure);
    }

    private static SmimeSigningCertificateValidator ValidatorFor(ProvisionedMaterialResolver material) =>
        new(material, new FakeTimeProvider(Now));

    private static void ProvidePem(ProvisionedMaterialResolver material, string chain, string privateKey)
    {
        material.ProvisionText(ChainReference, chain);
        material.ProvisionText(PrivateKeyReference, privateKey);
    }

    private static SmimeSigningCertificateOptions BundleOf(string? passwordReference = null) => new()
    {
        Bundle = Block(BundleReference, passwordReference),
    };

    private static SmimeSigningCertificateOptions PemOf(string? privateKeyPasswordReference = null) => new()
    {
        CertificateChain = Block(ChainReference),
        PrivateKey = Block(PrivateKeyReference, privateKeyPasswordReference),
    };

    private static ConfiguredSecret Block(string reference, string? passwordReference = null) => new()
    {
        Name = reference,
        SecretReference = reference,
        Password = passwordReference is null
            ? null
            : new ConfiguredSecret { Name = passwordReference, SecretReference = passwordReference },
    };
}
