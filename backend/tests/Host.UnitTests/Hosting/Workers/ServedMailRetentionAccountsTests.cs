// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Infrastructure.Persistence.Users.AccountSettings;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Workers;

/// <summary>Covers which accounts a walk is handed, and where the next page begins.</summary>
public sealed class ServedMailRetentionAccountsTests
{
    private static readonly Guid First = Guid.Parse("0199a0c0-0000-7000-8000-000000000001");
    private static readonly Guid Second = Guid.Parse("0199a0c0-0000-7000-8000-000000000002");

    /// <summary>A full page hands out every account it read and resumes after the last of them.</summary>
    [Fact]
    public async Task ReadPageAsync_AFullPage_HandsOutEveryAccountAndResumesAfterTheLast()
    {
        // Arrange
        var accounts = new ServedMailRetentionAccounts(Served(First, Second));

        // Act
        var page = await accounts.ReadPageAsync(after: null, limit: 2, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [MailAccountId.Create(First.ToString("D")), MailAccountId.Create(Second.ToString("D"))],
            page.Accounts);
        Assert.Equal(Second, page.ResumeAfter);
    }

    [Fact]
    public async Task ReadPageAsync_APageShortOfTheLimit_IsTheLast()
    {
        // Arrange
        var accounts = new ServedMailRetentionAccounts(Served(First));

        // Act
        var page = await accounts.ReadPageAsync(after: null, limit: 2, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(page.Accounts);
        Assert.Null(page.ResumeAfter);
    }

    private static IServedMailAccountReader Served(params Guid[] accounts)
    {
        var reader = Substitute.For<IServedMailAccountReader>();
        reader.ReadServedVersionsAsync(Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<ServedMailAccountVersion>>(
                [.. accounts.Select(static account => new ServedMailAccountVersion(account, 1))]));

        return reader;
    }
}
