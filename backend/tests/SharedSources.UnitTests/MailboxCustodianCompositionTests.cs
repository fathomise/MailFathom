// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Accounts.Custody;
using MailFathom.Application.Folders.Local;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the composition every suite builds the custodians and their collaborators through.</summary>
/// <remarks>
/// A fault here reports somebody else's arrangement: a part built over a port other than the one a test stated would
/// let that test assert against a double nothing reads, and two instances of one part would let a held account's
/// filer and its custodian disagree about which filer is the account's.
/// </remarks>
public sealed class MailboxCustodianCompositionTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("primary");

    /// <summary>The resolver reads the custody store a test states, so an arranged phase decides the custodian.</summary>
    [Fact]
    public async Task Custodians_OverAStatedCustodyStore_ResolveByWhatItAnswers()
    {
        // Arrange
        var custody = Substitute.For<IMailAccountCustodyStore>();
        custody
            .ReadAsync(Account, Arg.Any<CancellationToken>())
            .Returns(new MailAccountCustodyState(MailAccountCustody.HoldMailbox, MailAccountCustodyPhase.Held));
        var composition = new MailboxCustodianComposition(new FakeTimeProvider()) { Custody = custody };

        // Act
        var custodian = await composition.Custodians.ResolveAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(composition.Held, custodian);
    }

    /// <summary>A part is built once, so the filer a test reads back is the filer the held custodian files through.</summary>
    [Fact]
    public void LocalFiler_ReadTwice_IsOneInstance()
    {
        // Arrange
        var composition = new MailboxCustodianComposition(new FakeTimeProvider());

        // Act
        var first = composition.LocalFiler;
        var second = composition.LocalFiler;

        // Assert
        Assert.Same(first, second);
    }

    /// <summary>A filer a test builds over ports of its own is the one the composition hands out, built over its resolver.</summary>
    [Fact]
    public void LocalFiler_WithAStatedFactory_IsTheOneTheFactoryBuiltOverTheCompositionsResolver()
    {
        // Arrange
        MailboxCustodianResolver? handedResolver = null;
        var composition = new MailboxCustodianComposition(new FakeTimeProvider())
        {
            LocalFilerOver = custodians =>
            {
                handedResolver = custodians;

                return new MailboxCustodianComposition(new FakeTimeProvider()).LocalFiler;
            },
        };

        // Act
        _ = composition.LocalFiler;

        // Assert
        Assert.Same(composition.Custodians, handedResolver);
    }

    /// <summary>Every part reads the ports in force when it is first built, which is after the object initializer ran.</summary>
    [Fact]
    public async Task Held_OverAStatedFolderStore_ReadsThroughIt()
    {
        // Arrange
        var folders = Substitute.For<ILocalMailFolderStore>();
        var composition = new MailboxCustodianComposition(new FakeTimeProvider())
        {
            LocalFolders = folders,
            Authorization = AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead),
        };

        // Act
        await composition.Held.DescribeFoldersAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        await folders.Received().ReadAsync(Account, Arg.Any<CancellationToken>());
    }
}
