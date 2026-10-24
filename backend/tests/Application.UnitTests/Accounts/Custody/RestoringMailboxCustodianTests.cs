// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Local;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Accounts.Custody;

/// <summary>Covers how an account being restored to its source carries its acts: committed locally and carried to the source, with its folders left alone.</summary>
public sealed class RestoringMailboxCustodianTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("primary");

    private static readonly MailFolderResolution Inbox =
        MailFolderResolution.FirstBindingOf(MailFolderAlias.Create("INBOX"), RemoteFolderPath.Create("INBOX"));

    private static readonly StoredEmailId Email = StoredEmailId.Create(Guid.Parse("0199c1a2-0000-7000-8000-0000000000c1"));

    private static readonly EmailOccurrenceId Occurrence =
        EmailOccurrenceId.Create(Account, Inbox.Id, ImapUidValidity.Create(7), ImapUid.Create(42));

    private readonly InMemoryLocalMailFolderStore folders = new(Account, MailAccountCustodyPhase.Restoring);

    private readonly InMemoryMailboxMutationRecordStore records = new();

    private readonly InMemoryLocalEmailStateStore states = new(Account);

    /// <summary>A folder act during a restore would act on a mailbox half in each place, so every one is refused until it ends.</summary>
    [Fact]
    public async Task FolderActs_DuringARestore_AreEachRefusedAsNotHeld()
    {
        // Arrange
        var custodian = this.Composition().Restoring;
        var folder = MailFolderId.Mint(DateTimeOffset.UnixEpoch);

        // Act
        MailFolderActOutcome[] outcomes =
        [
            await custodian.CreateFolderAsync(Account, parent: null, "Plans", role: null, Token),
            await custodian.RenameFolderAsync(Account, folder, "Plans", Token),
            await custodian.MoveFolderAsync(Account, folder, parent: null, Token),
            await custodian.DeleteFolderAsync(Account, folder, Token),
        ];

        // Assert
        Assert.All(outcomes, outcome => Assert.Equal(MailFolderActRefusal.AccountNotHeld, outcome.Refusal));
        Assert.Equal(0, this.folders.SaveCount);
    }

    /// <summary>Its local folders are still the truth and are read as such, offering no act until the restore ends.</summary>
    [Fact]
    public async Task DescribeFoldersAsync_DuringARestore_OffersNoAct()
    {
        // Act
        var management = await this.Composition().Restoring.DescribeFoldersAsync(Account, Token);

        // Assert
        Assert.NotNull(management);
        Assert.Empty(management.AllowedActs);
        Assert.All(management.Folders, folder => Assert.Empty(folder.AllowedActs));
    }

    /// <summary>A change on a message the source still holds is committed at once and recorded for the source in the same transaction.</summary>
    [Fact]
    public async Task SubmitAsync_AChangeOnAMessageTheSourceHolds_CommitsItAndRecordsItAsAlreadyCommitted()
    {
        // Arrange
        this.states.Store(
            Email,
            new LocalEmailState(Inbox, HoldsSourceOccurrence: true, Folder: null, IsSeen: false, IsFlagged: false, RemoteEmailKeywords.Create([])));
        var change = new MailboxChange(
            MailboxMutationRequest.SetSeen(Email, Occurrence, MailboxMutationRequester.Command("call-1"), true),
            Destination: null,
            HeldUntil: null,
            PersonsMove: false,
            CrossesDraftsOrSentTreeOnTheServer: false,
            PreparedCopy: null);

        // Act
        var submitted = await this.Composition().Restoring.SubmitAsync(Substitute.For<IPersistenceSession>(), change, Token);

        // Assert
        Assert.Equal(MailboxChangeSubmissionOutcome.Applied, submitted.Outcome);
        Assert.True(this.states.States[Email].IsSeen);
        Assert.Equal(MailboxMutationLocalChange.AlreadyCommitted, submitted.Record!.LocalChange);
    }

    /// <summary>Its source is not drained, so a draft revision is appended there rather than filed locally.</summary>
    [Fact]
    public async Task PrepareDraftCopyAsync_DuringARestore_PreparesNoLocalCopy()
    {
        // Act
        var copy = await this.Composition().Restoring.PrepareDraftCopyAsync(Account, "Subject: draft\r\n\r\nbody"u8.ToArray(), Token);

        // Assert
        Assert.Null(copy);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private MailboxCustodianComposition Composition() => new(new FakeTimeProvider())
    {
        Custody = this.folders.Custody,
        LocalFolders = this.folders,
        Records = this.records,
        States = this.states,
        Authorization = AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead, MailFathomPermission.MailFoldersWrite),
    };
}
