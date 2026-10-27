// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.KnownAuthenticationProviders;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Mail.Readers;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Secrets.Discovery;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail;

/// <summary>Covers which server an account believes about who sent its mail, and what an unusable answer costs.</summary>
public sealed class TrustedAuthenticationAuthorityConfigurationTests
{
    /// <summary>The account's own authserv-id reaches the reading that selects a header, whatever case it was written in.</summary>
    [Fact]
    public async Task GetTrustedAuthorityAsync_ConfiguredAccount_AnswersWithTheServerItNamed()
    {
        // Arrange
        var account = CreateAccount("primary");
        account.TrustedAuthenticationServiceIdentifier = "MX.Example.Test";
        var reader = ReaderFor(KnownProviders(), account);

        // Act
        var authority = await reader.GetTrustedAuthorityAsync(MailAccountId.Create("primary"), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(authority.NamesAServer);
        Assert.True(authority.Produced("mx.example.test"));
        Assert.False(authority.Produced("attacker.test"));
    }

    /// <summary>An account naming no server, on a host the list has no entry for, believes no header at all.</summary>
    [Fact]
    public async Task GetTrustedAuthorityAsync_AccountNamingNoServerOnAnUnlistedHost_BelievesNothing()
    {
        // Arrange
        var reader = ReaderFor(KnownProviders(), CreateAccount("primary"));

        // Act
        var authority = await reader.GetTrustedAuthorityAsync(MailAccountId.Create("primary"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TrustedAuthenticationAuthority.None, authority);
    }

    /// <summary>An account naming no server believes the one its IMAP host's entry names, however the host was written.</summary>
    /// <param name="host">The host the account connects to.</param>
    [Theory]
    [InlineData("imap.gmail.com")]
    [InlineData("IMAP.Gmail.com")]
    [InlineData("imap.gmail.com.")]
    public async Task GetTrustedAuthorityAsync_AccountNamingNoServerOnAListedHost_BelievesTheListedServer(string host)
    {
        // Arrange
        var account = CreateAccount("primary");
        account.Host = host;
        var reader = ReaderFor(KnownProviders(Google), account);

        // Act
        var authority = await reader.GetTrustedAuthorityAsync(MailAccountId.Create("primary"), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(authority.Produced("mx.google.com"));
    }

    /// <summary>An account that names its own server keeps it, whatever the list says about its host.</summary>
    [Fact]
    public async Task GetTrustedAuthorityAsync_AccountNamingItsOwnServerOnAListedHost_KeepsItsOwn()
    {
        // Arrange
        var account = CreateAccount("primary");
        account.Host = "imap.gmail.com";
        account.TrustedAuthenticationServiceIdentifier = "gateway.example.test";
        var knownProviders = KnownProviders(Google);
        var reader = ReaderFor(knownProviders, account);

        // Act
        var authority = await reader.GetTrustedAuthorityAsync(MailAccountId.Create("primary"), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(authority.Produced("gateway.example.test"));
        Assert.False(authority.Produced("mx.google.com"));
        await knownProviders.DidNotReceiveWithAnyArgs().FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A host that only ends with a listed one is a different host, so no suffix of it matches.</summary>
    [Fact]
    public async Task GetTrustedAuthorityAsync_HostEndingWithAListedHost_BelievesNothing()
    {
        // Arrange
        var account = CreateAccount("primary");
        account.Host = "proxy.imap.gmail.com";
        var reader = ReaderFor(KnownProviders(Google), account);

        // Act
        var authority = await reader.GetTrustedAuthorityAsync(MailAccountId.Create("primary"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TrustedAuthenticationAuthority.None, authority);
    }

    /// <summary>One work unit reads a host's entry once, however many of its messages ask.</summary>
    [Fact]
    public async Task GetTrustedAuthorityAsync_SameHostAskedTwice_ReadsTheListOnce()
    {
        // Arrange
        var account = CreateAccount("primary");
        account.Host = "imap.gmail.com";
        var knownProviders = KnownProviders(Google);
        var reader = ReaderFor(knownProviders, account);

        // Act
        await reader.GetTrustedAuthorityAsync(MailAccountId.Create("primary"), TestContext.Current.CancellationToken);
        await reader.GetTrustedAuthorityAsync(MailAccountId.Create("primary"), TestContext.Current.CancellationToken);

        // Assert
        await knownProviders.Received(1).FindAsync("imap.gmail.com", Arg.Any<CancellationToken>());
    }

    /// <summary>An extraction may run over an account a reload removed, and believing nothing is the answer rather than failing.</summary>
    [Fact]
    public async Task GetTrustedAuthorityAsync_AccountThisSnapshotNoLongerNames_BelievesNothing()
    {
        // Arrange
        var reader = ReaderFor(KnownProviders(Google), CreateAccount("primary"));

        // Act
        var authority = await reader.GetTrustedAuthorityAsync(MailAccountId.Create("removed"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TrustedAuthenticationAuthority.None, authority);
    }

    /// <summary>A value that is present and unusable fails startup, because the alternative is mail that never authenticates.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("mx example test")]
    public void ValidateForSynchronization_UnusableTrustedServiceIdentifier_IsRefused(string configured)
    {
        // Arrange
        var account = CreateAccount("primary");
        account.TrustedAuthenticationServiceIdentifier = configured;

        // Act
        var messages = ConfiguredMailAccounts.Validate(OptionsFor(account)).Select(result => result.ErrorMessage).ToArray();

        // Assert
        Assert.Contains(
            messages,
            message => message!.Contains("trusted authentication service identifier", StringComparison.Ordinal));
    }

    /// <summary>The refusal names the account and never the value, which the failure rules refuse as a host name.</summary>
    [Fact]
    public void ValidateForSynchronization_UnusableTrustedServiceIdentifier_DoesNotEchoTheValue()
    {
        // Arrange
        var account = CreateAccount("primary");
        account.TrustedAuthenticationServiceIdentifier = "mx internal example test";

        // Act
        var refusal = Assert.Single(
            ConfiguredMailAccounts.Validate(OptionsFor(account)).Select(result => result.ErrorMessage),
            message => message!.Contains("trusted authentication service identifier", StringComparison.Ordinal));

        // Assert
        Assert.DoesNotContain("mx internal example test", refusal, StringComparison.Ordinal);
        Assert.Contains("primary", refusal, StringComparison.Ordinal);
    }

    /// <summary>Omitting the setting is not a mistake, so it produces no startup refusal.</summary>
    [Fact]
    public void ValidateForSynchronization_NoTrustedServiceIdentifier_IsAccepted()
    {
        // Act
        var messages = ConfiguredMailAccounts.Validate(OptionsFor(CreateAccount("primary")))
            .Select(result => result.ErrorMessage)
            .ToArray();

        // Assert
        Assert.DoesNotContain(
            messages,
            message => message!.Contains("trusted authentication service identifier", StringComparison.Ordinal));
    }

    private static KnownAuthenticationProvider Google => new("imap.gmail.com", "mx.google.com");

    private static ConfiguredTrustedAuthenticationAuthorityReader ReaderFor(
        IKnownAuthenticationProviderDirectory knownProviders,
        params MailSynchronizationAccountOptions[] accounts) =>
        new(OptionsFor(accounts), knownProviders);

    private static IKnownAuthenticationProviderDirectory KnownProviders(params KnownAuthenticationProvider[] entries)
    {
        var directory = Substitute.For<IKnownAuthenticationProviderDirectory>();
        directory.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => entries.FirstOrDefault(entry => entry.ImapHost == call.Arg<string>()));

        return directory;
    }

    private static MailSynchronizationOptions OptionsFor(params MailSynchronizationAccountOptions[] accounts) =>
        new MailSynchronizationOptions().Serving(accounts);

    private static MailSynchronizationAccountOptions CreateAccount(string accountId) => new()
    {
        AccountId = accountId,
        DisplayName = $"Account {accountId}",
        Host = "imap.example.test",
        UserName = "mailfathom@example.test",
        Secrets = new MailAccountSecretOptions
        {
            Password = new ConfiguredSecret { SecretReference = $"systemd-credential:imap-{accountId}-password" },
        },
    };
}
