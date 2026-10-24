// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Accounts.Custody;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Accounts.Custody;

/// <summary>Covers the one place an account's custody phase chooses who carries an act on its mailbox.</summary>
public sealed class MailboxCustodianResolverTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("primary");

    [Theory]
    [InlineData(MailAccountCustodyPhase.Mirrored, typeof(MirroredMailboxCustodian))]
    [InlineData(MailAccountCustodyPhase.Held, typeof(HeldMailboxCustodian))]
    [InlineData(MailAccountCustodyPhase.Restoring, typeof(RestoringMailboxCustodian))]
    public async Task ResolveAsync_EachPhase_ChoosesItsOwnCustodian(MailAccountCustodyPhase phase, Type expected)
    {
        // Arrange
        var store = new InMemoryLocalMailFolderStore(Account, phase);

        // Act
        var custodian = await store.Composing(new FakeTimeProvider()).Custodians.ResolveAsync(Account, Token);

        // Assert
        Assert.IsType(expected, custodian);
    }

    /// <summary>Only a held account places arrivals and filed copies in local folders; a restoring one is handing its folders back.</summary>
    [Theory]
    [InlineData(MailAccountCustodyPhase.Mirrored, false)]
    [InlineData(MailAccountCustodyPhase.Held, true)]
    [InlineData(MailAccountCustodyPhase.Restoring, false)]
    public async Task ResolveAsync_EachPhase_ChoosesACustodianThatSaysWhetherItPlacesMailInLocalFolders(
        MailAccountCustodyPhase phase,
        bool expected)
    {
        // Arrange
        var store = new InMemoryLocalMailFolderStore(Account, phase);

        // Act
        var custodian = await store.Composing(new FakeTimeProvider()).Custodians.ResolveAsync(Account, Token);

        // Assert
        Assert.Equal(expected, custodian.PlacesMailInLocalFolders);
    }

    /// <summary>A phase added without an arm here compiles, so it has to fail loudly on the first act rather than fall to a default custodian.</summary>
    [Fact]
    public async Task ResolveAsync_APhaseNoCustodianCarries_Throws()
    {
        // Arrange
        var custody = Substitute.For<IMailAccountCustodyStore>();
        custody
            .ReadAsync(Account, Arg.Any<CancellationToken>())
            .Returns(new MailAccountCustodyState(MailAccountCustody.HoldMailbox, (MailAccountCustodyPhase)99));
        var custodians = new MailboxCustodianComposition(new FakeTimeProvider()) { Custody = custody }.Custodians;

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => custodians.ResolveAsync(Account, Token));
    }

    /// <summary>An account whose row no synchronization has written yet starts mirrored, as its row would.</summary>
    [Fact]
    public async Task ResolveAsync_AnAccountWithNoCustodyRecorded_ChoosesTheMirroredCustodian()
    {
        // Arrange
        var custodians = new MailboxCustodianComposition(new FakeTimeProvider()).Custodians;

        // Act
        var custodian = await custodians.ResolveAsync(Account, Token);

        // Assert
        Assert.IsType<MirroredMailboxCustodian>(custodian);
    }

    /// <summary>A switch committing between the dispatch and the act is judged by the reading inside the act's own transaction.</summary>
    [Fact]
    public async Task ResolveAsync_InsideATransaction_ReadsThePhaseThroughThatTransaction()
    {
        // Arrange
        var session = Substitute.For<IPersistenceSession>();
        var custody = Substitute.For<IMailAccountCustodyStore>();
        custody
            .ReadAsync(Account, Arg.Any<CancellationToken>())
            .Returns(MailAccountCustodyState.Mirrored);
        custody
            .ReadAsync(session, Account, Arg.Any<CancellationToken>())
            .Returns(new MailAccountCustodyState(MailAccountCustody.HoldMailbox, MailAccountCustodyPhase.Held));
        var custodians = new MailboxCustodianComposition(new FakeTimeProvider()) { Custody = custody }.Custodians;

        // Act
        var custodian = await custodians.ResolveAsync(session, Account, Token);

        // Assert
        Assert.IsType<HeldMailboxCustodian>(custodian);
    }

    /// <summary>Two acts in one scope reach one custodian, so the collaborators it composes are built once.</summary>
    [Fact]
    public async Task ResolveAsync_TwiceInOneScope_ChoosesTheSameInstance()
    {
        // Arrange
        var custodians = new InMemoryLocalMailFolderStore(Account, MailAccountCustodyPhase.Held)
            .Composing(new FakeTimeProvider())
            .Custodians;

        // Act
        var first = await custodians.ResolveAsync(Account, Token);
        var second = await custodians.ResolveAsync(Account, Token);

        // Assert
        Assert.Same(first, second);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
