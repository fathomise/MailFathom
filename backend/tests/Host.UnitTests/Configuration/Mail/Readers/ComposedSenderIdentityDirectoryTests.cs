// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Mail.Readers;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail.Readers;

public sealed class ComposedSenderIdentityDirectoryTests
{
    private static readonly MailAccountId Colleague = MailAccountId.Create("primary");

    /// <summary>
    /// A colleague's account is assigned to somebody the work unit does not act for, so its identity is read from that
    /// account's own settings rather than from the snapshot the work unit was prepared with.
    /// </summary>
    [Fact]
    public async Task FindSenderIdentityAsync_AccountOfAUserTheWorkUnitDoesNotActFor_ReadsItFromThatAccountsOwnSettings()
    {
        // Arrange
        var account = ConfiguredMailAccounts.Primary();
        account.Delivery = ConfiguredMailAccounts.Delivery();
        var accounts = Substitute.For<IMailSynchronizationAccountSource>();
        accounts.ReadRunSettingsAsync(Colleague, null, Arg.Any<CancellationToken>())
            .Returns(ConfiguredMailAccounts.Holding(account));
        var directory = new ComposedSenderIdentityDirectory(accounts);

        // Act
        var first = await directory.FindSenderIdentityAsync(Colleague, TestContext.Current.CancellationToken);
        var second = await directory.FindSenderIdentityAsync(Colleague, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("mailfathom@example.test", first?.Address.Address);
        Assert.Equal(first, second);
        await accounts.Received(1).ReadRunSettingsAsync(Colleague, null, Arg.Any<CancellationToken>());
    }

    /// <summary>An account synchronization does not serve has no settings to compose, so it sends as nobody.</summary>
    [Fact]
    public async Task FindSenderIdentityAsync_AccountNotServed_ReadsNothing()
    {
        // Arrange
        var accounts = Substitute.For<IMailSynchronizationAccountSource>();
        accounts.ReadRunSettingsAsync(Colleague, null, Arg.Any<CancellationToken>())
            .Returns((MailSynchronizationOptions?)null);

        // Act
        var identity = await new ComposedSenderIdentityDirectory(accounts)
            .FindSenderIdentityAsync(Colleague, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(identity);
    }
}
