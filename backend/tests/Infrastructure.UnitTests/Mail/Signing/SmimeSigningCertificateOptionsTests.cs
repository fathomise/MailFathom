// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Mail.Signing;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Signing;

public sealed class SmimeSigningCertificateOptionsTests
{
    private const string Path = "Delivery:SmimeSigningCertificate";

    [Fact]
    public void FindConfigurationErrors_NothingConfigured_ReportsNothingBecauseTheAccountSignsWithoutSmime()
    {
        // Arrange
        var options = new SmimeSigningCertificateOptions();

        // Act
        var errors = options.FindConfigurationErrors(Path);

        // Assert
        Assert.Empty(errors);
        Assert.False(options.IsConfigured);
    }

    [Fact]
    public void FindConfigurationErrors_BundleBesidePemMaterial_RefusesTheAmbiguity()
    {
        // Arrange
        var options = new SmimeSigningCertificateOptions { Bundle = Block("bundle"), CertificateChain = Block("chain") };

        // Act
        var errors = options.FindConfigurationErrors(Path);

        // Assert
        Assert.StartsWith($"{Path} — a PKCS#12 bundle and separate PEM material", Assert.Single(errors), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false, "Delivery:SmimeSigningCertificate:PrivateKey")]
    [InlineData(false, true, "Delivery:SmimeSigningCertificate:CertificateChain")]
    public void FindConfigurationErrors_HalfOfThePemPair_NamesTheMissingHalf(bool chain, bool privateKey, string expectedPath)
    {
        // Arrange
        var options = new SmimeSigningCertificateOptions
        {
            CertificateChain = chain ? Block("chain") : null,
            PrivateKey = privateKey ? Block("key") : null,
        };

        // Act
        var errors = options.FindConfigurationErrors(Path);

        // Assert
        Assert.StartsWith($"{expectedPath} — ", Assert.Single(errors), StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_CompletePemPair_ReportsNothing()
    {
        // Arrange
        var options = new SmimeSigningCertificateOptions { CertificateChain = Block("chain"), PrivateKey = Block("key") };

        // Act
        var errors = options.FindConfigurationErrors(Path);

        // Assert
        Assert.Empty(errors);
        Assert.True(options.IsConfigured);
    }

    private static ConfiguredSecret Block(string name) => new() { Name = name, SecretReference = $"file:/etc/mailfathom/{name}" };
}
