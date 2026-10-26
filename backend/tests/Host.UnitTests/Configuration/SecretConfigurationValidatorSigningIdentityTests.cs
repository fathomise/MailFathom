// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Persistence;
using MailFathom.Host.UnitTests.Configuration.Mail;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Mail.Signing;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Secrets.Discovery;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration;

/// <summary>Covers that a user's mail accounts have their signing identities read as material, and that a refusal names the setting and never the material.</summary>
/// <remarks>Which material each validator accepts is the Infrastructure suite's; what is proven here is that the walk reaches them and how it reports them.</remarks>
public sealed class SecretConfigurationValidatorSigningIdentityTests
{
    private const string RecordPath = "Users:0";

    [Fact]
    public async Task FindUserMailAccountErrorsAsync_SigningIdentitiesThatAreNotKeys_NamesEachSettingAndTheFailureAndNoMaterial()
    {
        // Arrange
        var account = AccountReadingWithAPlaintextPassword();
        account.Delivery = ConfiguredMailAccounts.Delivery();
        account.Delivery.SmimeSigningCertificate = new SmimeSigningCertificateOptions
        {
            CertificateChain = Block("smime-chain", "plaintext:chain-material-text"),
            PrivateKey = Block("smime-key", "plaintext:key-material-text"),
        };
        account.Delivery.OpenPgpSigningKey = Block("openpgp-key", "plaintext:openpgp-material-text");

        // Act
        var errors = await CreateValidator().FindUserMailAccountErrorsAsync(
            RecordPath,
            [account],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [
                $"{RecordPath}:MailAccounts:0:Delivery:SmimeSigningCertificate — the S/MIME signing certificate cannot sign mail from this account [{CertificateMaterialFailure.EncodingNotRecognized}].",
                $"{RecordPath}:MailAccounts:0:Delivery:OpenPgpSigningKey — the OpenPGP signing key cannot sign mail from this account [{OpenPgpSigningKeyFailure.NotAnArmoredSecretKey}].",
            ],
            errors);
        Assert.DoesNotContain(errors, error => error.Contains("material-text", StringComparison.Ordinal));
    }

    /// <summary>An identity provisioned with no endpoint is refused by the account's own rules, so reading its material too would report one mistake twice.</summary>
    [Fact]
    public async Task FindUserMailAccountErrorsAsync_SigningIdentityWithoutASubmissionEndpoint_LeavesItToTheAccountRules()
    {
        // Arrange
        var account = AccountReadingWithAPlaintextPassword();
        account.Delivery.OpenPgpSigningKey = Block("openpgp-key", "plaintext:openpgp-material-text");

        // Act
        var errors = await CreateValidator().FindUserMailAccountErrorsAsync(
            RecordPath,
            [account],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(errors);
    }

    private static MailSynchronizationAccountOptions AccountReadingWithAPlaintextPassword()
    {
        var account = ConfiguredMailAccounts.Primary();
        account.Secrets = new MailAccountSecretOptions { Password = Block("imap-password", "plaintext:imap-password") };

        return account;
    }

    private static SecretConfigurationValidator CreateValidator()
    {
        var resolver = new PlaintextOnlySecretReferenceResolver();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero));

        return new SecretConfigurationValidator(
            resolver,
            new TrustAnchorLoader(resolver),
            new SmimeSigningCertificateValidator(resolver, clock),
            new OpenPgpSigningKeyValidator(resolver, clock),
            new DatabaseConnectionSettingsMapper(new ConfigurationBuilder().Build()),
            new StubDatabaseConnectionSettingsValidator(),
            PostgresTextSearchConfiguration.Default,
            new DatabaseCommandTimeout(TimeSpan.FromSeconds(HostApplicationBuilderExtensions.DefaultDatabaseCommandTimeoutSeconds)),
            clock,
            new RecordingLogger<SecretConfigurationValidator>());
    }

    private static ConfiguredSecret Block(string name, string reference) => new() { Name = name, SecretReference = reference };
}
