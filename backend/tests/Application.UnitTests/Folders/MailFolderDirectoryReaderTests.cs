// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.EmailContent;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Screening;
using MailFathom.Application.Mail.Delivery.Tracking;
using MailFathom.Application.Synchronization.Administration;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Folders;

/// <summary>Covers the one read a mail screen draws its tree from: which mailboxes, which folders, and how current each is.</summary>
/// <remarks>
/// Whose accounts these are, what a state means, and what a caller without the grant is answered are decided in the
/// reading this composes and are covered there, and each folder's place, role, and counts are the folder row's. What is
/// asserted here is what only this use case decides: that every row is published, in the tree's order, with the
/// freshness of the alias that feeds it.
/// </remarks>
public sealed class MailFolderDirectoryReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);

    private static readonly ServedMailAccount Work = SyntheticServedAccount.Of("work");

    private static readonly ServedMailAccount Private = SyntheticServedAccount.Of("private");

    /// <summary>The user story: several mailboxes and their folders arrive as one tree rather than as one request per mailbox.</summary>
    [Fact]
    public async Task ReadAsync_AUserWithSeveralMailboxes_AnswersEveryOneOfThemWithItsFoldersInOneRead()
    {
        // Arrange
        var reader = ReaderOver(
            Freshness((Work.Id, "inbox", Now), (Private.Id, "inbox", Now)),
            OwningAccounts(Work, Private),
            StoredFolders(
                Stored(Work.Id, 1, "inbox", ["INBOX"], storedEmailCount: 12, unreadEmailCount: 3),
                Stored(Private.Id, 2, "inbox", ["INBOX"], storedEmailCount: 4)));

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([Work, Private], directory.Accounts.Select(account => account.Account.Account));
        Assert.Equal([1, 1], directory.Accounts.Select(account => account.Folders.Count));
    }

    /// <summary>A caller that may read the outbox is told how much each account's outbox holds, so it can leave an empty one out.</summary>
    [Fact]
    public async Task ReadAsync_ACallerHoldingBothOutboxGrants_CarriesEachAccountsOutboxCount()
    {
        // Arrange
        var outbox = Substitute.For<IOutboxOperationStore>();
        outbox
            .CountAccountOutboxesAsync(Arg.Any<IReadOnlyCollection<MailAccountId>>(), Arg.Any<CancellationToken>())
            .Returns([new AccountOutboxCount(Work.Id, Waiting: 2, Refused: 1), new AccountOutboxCount(Private.Id, 0, 0)]);
        var reader = ReaderOver(
            Freshness((Work.Id, "inbox", Now), (Private.Id, "inbox", Now)),
            OwningAccounts(Work, Private),
            StoredFolders(Stored(Work.Id, 1, "inbox", ["INBOX"]), Stored(Private.Id, 2, "inbox", ["INBOX"])),
            StubMailFolderMappings.Nothing,
            Ledger(),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead, MailFathomPermission.MailSend),
            outbox);

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [new AccountOutboxCount(Work.Id, 2, 1), new AccountOutboxCount(Private.Id, 0, 0)],
            directory.Accounts.Select(account => account.Outbox));
    }

    /// <summary>A caller that may not read the outbox is not told its size, and nothing counts it.</summary>
    [Fact]
    public async Task ReadAsync_ACallerHoldingOnlyTheReadingGrant_CarriesNoOutboxAndCountsNothing()
    {
        // Arrange
        var outbox = Substitute.For<IOutboxOperationStore>();
        var reader = ReaderOver(
            Freshness((Work.Id, "inbox", Now)),
            OwningAccounts(Work),
            StoredFolders(Stored(Work.Id, 1, "inbox", ["INBOX"])),
            StubMailFolderMappings.Nothing,
            Ledger(),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead),
            outbox);

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(Assert.Single(directory.Accounts).Outbox);
        Assert.Empty(outbox.ReceivedCalls());
    }

    /// <summary>The counts are what the store answered, and they reach the folder they were counted for.</summary>
    [Fact]
    public async Task ReadAsync_AFolderHoldingMail_CarriesWhatIsStoredAndWhatIsUnread()
    {
        // Arrange
        var reader = ReaderOver(
            Freshness((Work.Id, "inbox", Now)),
            OwningAccounts(Work),
            StoredFolders(Stored(Work.Id, 1, "inbox", ["INBOX"], storedEmailCount: 12, unreadEmailCount: 3)));

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        var folder = Assert.Single(Assert.Single(directory.Accounts).Folders);
        Assert.Equal(12, folder.StoredEmailCount);
        Assert.Equal(3, folder.UnreadEmailCount);
    }

    /// <summary>
    /// The story a client cannot serve itself: which folder is the sent one is the service's answer, because a server
    /// advertises the role by attribute and names the folder in whatever language it likes.
    /// </summary>
    [Fact]
    public async Task ReadAsync_AFolderRowCarryingARole_PublishesTheRoleRatherThanLeavingItToBeGuessed()
    {
        // Arrange
        var reader = ReaderOver(
            Freshness((Work.Id, "2024", Now), (Work.Id, "sent", Now)),
            OwningAccounts(Work),
            StoredFolders(
                Stored(Work.Id, 1, "2024", ["Archiwum", "2024"]),
                Stored(Work.Id, 2, "sent", ["Gesendete Objekte"], role: MailFolderSpecialUse.Sent)));

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [null, MailFolderSpecialUse.Sent],
            Assert.Single(directory.Accounts).Folders.Select(folder => folder.Role));
    }

    /// <summary>A tree is drawn in the order of the folders' places in it, whatever order the store answered them in.</summary>
    [Fact]
    public async Task ReadAsync_FoldersAnsweredOutOfOrder_PublishesThemInTheOrderOfTheirPlaceInTheTree()
    {
        // Arrange
        var reader = ReaderOver(
            Freshness(),
            OwningAccounts(Work),
            StoredFolders(
                Stored(Work.Id, 3, folderAlias: null, ["Projects", "Alpha"]),
                Stored(Work.Id, 2, folderAlias: null, ["Projects"]),
                Stored(Work.Id, 1, "inbox", ["INBOX"])));

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [IdOf(1), IdOf(2), IdOf(3)],
            Assert.Single(directory.Accounts).Folders.Select(folder => folder.Id));
        Assert.Equal(
            ["Projects", "Alpha"],
            Assert.Single(directory.Accounts).Folders[2].HierarchyLevels);
    }

    /// <summary>A folder an alias feeds carries the freshness the composed reading reported for that alias.</summary>
    [Fact]
    public async Task ReadAsync_AFolderAnAliasFeeds_CarriesThatAliasesFreshness()
    {
        // Arrange
        var reader = ReaderOver(
            Freshness((Work.Id, "inbox", Now)),
            OwningAccounts(Work),
            StoredFolders(Stored(Work.Id, 1, "inbox", ["INBOX"])));

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        var freshness = Assert.Single(Assert.Single(directory.Accounts).Folders).Freshness;
        Assert.NotNull(freshness);
        Assert.Equal(Now, freshness.SynchronizedAt);
    }

    /// <summary>
    /// The user story behind issue 1758: a folder configuration declares and nothing synchronizes still has a row and a
    /// role, and it is published as never synchronized rather than left out.
    /// </summary>
    [Fact]
    public async Task ReadAsync_AFolderAnAliasFeedsThatNoRunHasReached_IsPublishedAsNeverSynchronized()
    {
        // Arrange
        var reader = ReaderOver(
            Freshness((Work.Id, "inbox", Now)),
            OwningAccounts(Work),
            StoredFolders(Stored(Work.Id, 1, "trash", ["Trash"], role: MailFolderSpecialUse.Trash)));

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        var trash = Assert.Single(Assert.Single(directory.Accounts).Folders);
        Assert.Equal(MailFolderSpecialUse.Trash, trash.Role);
        Assert.Equal(MailSynchronizationState.NeverSynchronized, trash.Freshness?.State);
    }

    /// <summary>A folder no alias feeds is one MailFathom keeps itself, so nothing synchronizes it and it carries no freshness.</summary>
    [Fact]
    public async Task ReadAsync_AFolderNoAliasFeeds_CarriesNoFreshness()
    {
        // Arrange
        var reader = ReaderOver(
            Freshness((Work.Id, "inbox", Now)),
            OwningAccounts(Work),
            StoredFolders(Stored(Work.Id, 1, folderAlias: null, ["Projects"], storedEmailCount: 2)));

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        var folder = Assert.Single(Assert.Single(directory.Accounts).Folders);
        Assert.Null(folder.Freshness);
        Assert.Equal(2, folder.StoredEmailCount);
    }

    /// <summary>The folder's freshness is the composed reading's own, so the tree and the mailbox list beside it cannot disagree.</summary>
    [Fact]
    public async Task ReadAsync_AFolderWhoseTurnFailed_CarriesTheSameReadingTheMailboxListWouldGive()
    {
        // Arrange
        var ledger = Ledger();
        ledger.RecordFolderUnsynchronized(
            new MailFolderIdentity(Work.Id, MailFolderAlias.Create("inbox")),
            MailFolderRunOutcome.DeferredAfterMailServerUnavailable);
        var reader = ReaderOver(
            Freshness((Work.Id, "inbox", Now)),
            OwningAccounts(Work),
            StoredFolders(Stored(Work.Id, 1, "inbox", ["INBOX"], storedEmailCount: 12, unreadEmailCount: 3)),
            StubMailFolderMappings.Nothing,
            ledger);

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        var account = Assert.Single(directory.Accounts);
        Assert.Equal(MailSynchronizationState.Unreachable, account.Account.State);
        Assert.Equal(MailSynchronizationState.Unreachable, Assert.Single(account.Folders).Freshness?.State);
    }

    /// <summary>A user who owns no account reads an empty tree, and nothing counts mail on their behalf.</summary>
    [Fact]
    public async Task ReadAsync_AUserOwningNoAccount_AnswersAnEmptyTreeWithoutCountingAnything()
    {
        // Arrange
        var storedFolders = StoredFolders();
        var reader = ReaderOver(Freshness(), OwningAccounts(), storedFolders);

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(directory.Accounts);
        await storedFolders.DidNotReceive().ReadAsync(Arg.Any<MailboxScope>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The deployment's own switch travels with the tree, because no per-folder value says whether anything is still refreshing it.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReadAsync_ADeploymentThatSwitchedSynchronizationOff_SaysSoBesideTheTree(bool synchronizationEnabled)
    {
        // Arrange
        var reader = ReaderOver(
            Freshness((Work.Id, "inbox", Now)),
            OwningAccounts(synchronizationEnabled, Work),
            StoredFolders(Stored(Work.Id, 1, "inbox", ["INBOX"], storedEmailCount: 1)));

        // Act
        var directory = await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(synchronizationEnabled, directory.SynchronizationEnabled);
    }

    /// <summary>
    /// Naming a user's folders is the same disclosure as naming their mailboxes, so a credential without the mailbox
    /// grant is refused here exactly as it is there, and before anything is counted.
    /// </summary>
    [Fact]
    public async Task ReadAsync_ACallerWithoutTheMailboxGrant_IsRefusedRatherThanAnsweredWithAnEmptyTree()
    {
        // Arrange
        var reader = ReaderOver(
            Freshness(),
            OwningAccounts(Work),
            StoredFolders(),
            StubMailFolderMappings.Nothing,
            Ledger(),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.AssistantUse));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            reader.ReadAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailRead, refusal.RequiredPermission);
    }

    /// <summary>The folder tree is the person's own, so a folder withheld from AI is among the folders it is drawn from.</summary>
    [Fact]
    public async Task ReadAsync_AFolderWithheldFromAI_IsInTheScopeTheTreeIsDrawnFrom()
    {
        // Arrange
        var account = SyntheticServedAccount.Of("personal");
        var withheld = new MailFolderIdentity(account.Id, MailFolderAlias.Create("Private"));
        var storedFolders = StoredFolders();
        var reader = ReaderOver(
            Freshness(),
            OwningAccounts(account),
            storedFolders,
            folderParticipation: StubMailFolderParticipation.Mapping(withheld).Hiding(withheld));

        // Act
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        await storedFolders.Received().ReadAsync(
            Arg.Is<MailboxScope>(scope => scope.ReadableFolders.Contains(withheld)),
            Arg.Any<CancellationToken>());
    }

    private static MailSynchronizationRunLedger Ledger() => new(new FakeTimeProvider(Now));

    /// <summary>Answers the accounts the caller's user owns, and whether the deployment refreshes them.</summary>
    private static ICallerMailAccountCatalog OwningAccounts(params ServedMailAccount[] ownedAccounts) =>
        OwningAccounts(synchronizationEnabled: true, ownedAccounts);

    private static ICallerMailAccountCatalog OwningAccounts(
        bool synchronizationEnabled,
        params ServedMailAccount[] ownedAccounts)
    {
        var catalog = Substitute.For<ICallerMailAccountCatalog>();
        catalog.SynchronizationEnabled.Returns(synchronizationEnabled);
        catalog.ReadAssignedAccountsAsync(Arg.Any<CancellationToken>()).Returns([.. ownedAccounts]);

        return catalog;
    }

    /// <summary>Builds one folder row of what local state holds.</summary>
    private static StoredMailFolder Stored(
        MailAccountId accountId,
        int number,
        string? folderAlias,
        IReadOnlyList<string> hierarchyLevels,
        MailFolderSpecialUse? role = null,
        int storedEmailCount = 0,
        int unreadEmailCount = 0) =>
        new(
            accountId,
            IdOf(number),
            folderAlias is null ? null : MailFolderAlias.Create(folderAlias),
            role,
            hierarchyLevels,
            storedEmailCount,
            unreadEmailCount);

    private static MailFolderId IdOf(int number) =>
        MailFolderId.Create(Guid.Parse($"0199c1a2-0000-7000-8000-{number:D12}"));

    /// <summary>Answers where each folder sits on its mail server and how much of it is stored.</summary>
    private static IStoredMailFolderReader StoredFolders(params StoredMailFolder[] folders)
    {
        var reader = Substitute.For<IStoredMailFolderReader>();
        reader.ReadAsync(Arg.Any<MailboxScope>(), Arg.Any<CancellationToken>()).Returns(_ => folders);

        return reader;
    }

    /// <summary>Answers the folders local state knows of, which is what a tree's folder list is composed from.</summary>
    private static ISynchronizationFreshnessReader Freshness(
        params (MailAccountId AccountId, string FolderAlias, DateTimeOffset? SynchronizedAt)[] folders)
    {
        var freshnessReader = Substitute.For<ISynchronizationFreshnessReader>();
        freshnessReader
            .ReadAsync(Arg.Any<MailboxScope>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            [
                .. folders.Select(folder => new MailboxFolderFreshness(
                    folder.AccountId,
                    MailFolderAlias.Create(folder.FolderAlias),
                    folder.SynchronizedAt)),
            ]);

        return freshnessReader;
    }

    private static MailFolderDirectoryReader ReaderOver(
        ISynchronizationFreshnessReader freshnessReader,
        ICallerMailAccountCatalog catalog,
        IStoredMailFolderReader storedFolders,
        StubMailFolderMappings? mappings = null,
        MailSynchronizationRunLedger? runLedger = null,
        StubMailFolderParticipation? folderParticipation = null) =>
        ReaderOver(
            freshnessReader,
            catalog,
            storedFolders,
            mappings ?? StubMailFolderMappings.Nothing,
            runLedger ?? Ledger(),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead),
            folderParticipation: folderParticipation);

    private static MailFolderDirectoryReader ReaderOver(
        ISynchronizationFreshnessReader freshnessReader,
        ICallerMailAccountCatalog catalog,
        IStoredMailFolderReader storedFolders,
        StubMailFolderMappings mappings,
        MailSynchronizationRunLedger runLedger,
        AccessAuthorization authorization,
        IOutboxOperationStore? outboxOperations = null,
        StubMailFolderParticipation? folderParticipation = null)
    {
        var participation = folderParticipation ?? StubMailFolderParticipation.Nothing;
        var scopeResolver = new MailboxScopeResolver(
            catalog,
            participation,
            StubDeploymentMailFolders.Of(participation),
            mappings.Resolver);

        return new MailFolderDirectoryReader(
            new MailAccountFreshnessReader(
                new MailAccountDirectoryReader(
                    catalog,
                    freshnessReader,
                    storedFolders,
                    scopeResolver,
                    new RecordingMailboxReadTelemetry(),
                    authorization),
                runLedger),
            scopeResolver,
            storedFolders,
            new UserOutbox(
                catalog,
                new InMemoryOutgoingEmailStore(),
                outboxOperations ?? Substitute.For<IOutboxOperationStore>(),
                Substitute.For<IEmailContentStore>(),
                Substitute.For<IEmailContentRenderer>(),
                Substitute.For<IOutgoingMailTextReader>(),
                SensitiveContentEgressGuards.Inactive(),
                new EmailContentReadOptions(),
                Substitute.For<IUserLanguages>(),
                authorization,
                ClientSignalPublishers.ReachingNobody,
                TimeProvider.System));
    }
}
