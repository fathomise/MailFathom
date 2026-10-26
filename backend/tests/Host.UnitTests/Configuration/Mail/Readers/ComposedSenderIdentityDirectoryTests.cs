// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Mail.Readers;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail.Readers;

public sealed class ComposedSenderIdentityDirectoryTests
{
    private static readonly UserId Colleague = UserId.Create(Guid.Parse("0199f000-0000-7000-8000-0000000000c1"));
    private static readonly UserId Bystander = UserId.Create(Guid.Parse("0199f000-0000-7000-8000-0000000000c2"));

    /// <summary>
    /// A colleague is a user the work unit does not act for, so their accounts' identities come from their own settings,
    /// composed once per user however often the user is named.
    /// </summary>
    [Fact]
    public async Task ReadServedIdentitiesAsync_UserTheWorkUnitDoesNotActFor_ReadsTheirAccountsFromTheirOwnSettingsOnce()
    {
        // Arrange
        var account = ConfiguredMailAccounts.Primary();
        account.Delivery = ConfiguredMailAccounts.Delivery();
        var accounts = Substitute.For<IMailSynchronizationAccountSource>();
        accounts.ReadUserSettingsAsync(Colleague, Arg.Any<CancellationToken>()).Returns(ConfiguredMailAccounts.Holding(account));
        var directory = new ComposedSenderIdentityDirectory(accounts);

        // Act
        var identities = await directory.ReadServedIdentitiesAsync([Colleague, Colleague], TestContext.Current.CancellationToken);

        // Assert
        var identity = Assert.Single(identities);
        Assert.Equal(MailAccountId.Create("primary"), identity.Key);
        Assert.Equal("mailfathom@example.test", identity.Value.Address.Address);
        await accounts.Received(1).ReadUserSettingsAsync(Colleague, Arg.Any<CancellationToken>());
    }

    /// <summary>An account that configures no address to send from sends as nobody, so it is left out rather than answered empty.</summary>
    [Fact]
    public async Task ReadServedIdentitiesAsync_AccountSendingNothing_IsLeftOut()
    {
        // Arrange
        var accounts = Substitute.For<IMailSynchronizationAccountSource>();
        accounts.ReadUserSettingsAsync(Bystander, Arg.Any<CancellationToken>())
            .Returns(ConfiguredMailAccounts.Holding(ConfiguredMailAccounts.Primary()));

        // Act
        var identities = await new ComposedSenderIdentityDirectory(accounts)
            .ReadServedIdentitiesAsync([Bystander], TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(identities);
    }
}
