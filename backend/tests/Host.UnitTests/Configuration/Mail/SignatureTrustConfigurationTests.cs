// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Secrets.References;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.Infrastructure.Secrets.Sources;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail;

/// <summary>That the authorities and keys signature verification trusts are loaded when the configuration is read.</summary>
/// <remarks>
/// A reference that resolves to something that is not a certificate or an OpenPGP key would pass a reference check and
/// then trust nothing, so each is refused at startup and on reload under the position an operator wrote it at.
/// </remarks>
public sealed class SignatureTrustConfigurationTests
{
    [Fact]
    public async Task FindMailConfigurationErrorsAsync_AuthorityThatIsNotACertificate_IsRefusedAtItsPosition()
    {
        // Arrange
        var options = new MailSynchronizationOptions();
        options.SignatureVerification.TrustedAuthorities.Add(new ConfiguredSecret { Name = "company-mail-ca", SecretReference = "env:not-a-certificate" });

        // Act
        var errors = await SecretValidation.OverRegisteredSchemes()
            .FindMailConfigurationErrorsAsync(options, TestContext.Current.CancellationToken);

        // Assert
        var error = Assert.Single(errors);
        Assert.StartsWith("MailSynchronization:SignatureVerification:TrustedAuthorities:0", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindMailConfigurationErrorsAsync_OpenPgpKeyThatIsNotAKey_IsRefusedAtItsPosition()
    {
        // Arrange
        var options = new MailSynchronizationOptions();
        options.SignatureVerification.TrustedOpenPgpKeys.Add(new ConfiguredSecret { Name = "release-key", SecretReference = "env:not-a-key" });

        // Act
        var errors = await SecretValidation.OverRegisteredSchemes()
            .FindMailConfigurationErrorsAsync(options, TestContext.Current.CancellationToken);

        // Assert
        var error = Assert.Single(errors);
        Assert.StartsWith("MailSynchronization:SignatureVerification:TrustedOpenPgpKeys:0", error, StringComparison.Ordinal);
        Assert.Contains("not an OpenPGP public key", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindMailConfigurationErrorsAsync_OneOpenPgpKeyConfiguredTwice_IsRefusedAtTheSecondPositionNamingTheFirst()
    {
        // Arrange
        var resolver = Substitute.For<ISecretReferenceResolver>();
        resolver.ResolveAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => SecretResolutionResult.Resolved(ResolvedSecret.FromBytes(RobertsKey()), SecretMaterialSource.SchemeAdapter));
        var options = new MailSynchronizationOptions();
        options.SignatureVerification.TrustedOpenPgpKeys.Add(new ConfiguredSecret { Name = "release-key", SecretReference = "env:release-key" });
        options.SignatureVerification.TrustedOpenPgpKeys.Add(new ConfiguredSecret { Name = "release-key-refreshed", SecretReference = "env:release-key-refreshed" });

        // Act
        var errors = await SecretValidation.Over(resolver)
            .FindMailConfigurationErrorsAsync(options, TestContext.Current.CancellationToken);

        // Assert
        var error = Assert.Single(errors);
        Assert.StartsWith("MailSynchronization:SignatureVerification:TrustedOpenPgpKeys:1", error, StringComparison.Ordinal);
        Assert.Contains("MailSynchronization:SignatureVerification:TrustedOpenPgpKeys:0", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindMailConfigurationErrorsAsync_NothingTrustedBeyondTheSystemStore_ReportsNothing()
    {
        // Act
        var errors = await SecretValidation.OverRegisteredSchemes()
            .FindMailConfigurationErrorsAsync(new MailSynchronizationOptions(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(errors);
    }

    private static byte[] RobertsKey()
    {
        using var resource = typeof(SignatureTrustConfigurationTests).Assembly.GetManifestResourceStream("Signatures.pgp-robert.asc")!;
        using var copy = new MemoryStream();
        resource.CopyTo(copy);

        return copy.ToArray();
    }
}
