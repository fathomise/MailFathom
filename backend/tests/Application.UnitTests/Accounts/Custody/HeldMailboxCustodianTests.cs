// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Destinations;
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

/// <summary>Covers how an account MailFathom holds alone carries its acts: committed locally, with nothing asked of a server.</summary>
public sealed class HeldMailboxCustodianTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("primary");

    private static readonly MailFolderResolution Inbox =
        MailFolderResolution.FirstBindingOf(MailFolderAlias.Create("INBOX"), RemoteFolderPath.Create("INBOX"));

    private static readonly StoredEmailId Email = StoredEmailId.Create(Guid.Parse("0199c1a2-0000-7000-8000-0000000000b1"));

    private static readonly EmailOccurrenceId Occurrence =
        EmailOccurrenceId.Create(Account, Inbox.Id, ImapUidValidity.Create(7), ImapUid.Create(42));

    private readonly InMemoryLocalMailFolderStore folders = new(Account, MailAccountCustodyPhase.Held);

    private readonly InMemoryMailboxMutationRecordStore records = new();

    private readonly InMemoryLocalEmailStateStore states = new(Account);

    /// <summary>A change on a message commits to stored state in the caller's transaction, and the drained source is owed nothing.</summary>
    [Fact]
    public async Task SubmitAsync_AChangeOnAMessage_CommitsItLocallyWithoutARecord()
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
        var submitted = await this.Composition().Held.SubmitAsync(Substitute.For<IPersistenceSession>(), change, Token);

        // Assert
        Assert.Equal(MailboxChangeSubmissionOutcome.Applied, submitted.Outcome);
        Assert.True(this.states.States[Email].IsSeen);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>Every role already has its folder on a held account, so a creation naming one asks for what is there.</summary>
    [Fact]
    public async Task CreateFolderAsync_NamingARole_IsRefusedAsAlreadyPlayed()
    {
        // Act
        var outcome = await this.Composition().Held.CreateFolderAsync(
            Account,
            parent: null,
            "Elsewhere",
            MailFolderSpecialUse.Archive,
            Token);

        // Assert
        Assert.Equal(MailFolderActRefusal.RoleAlreadyPlayed, outcome.Refusal);
        Assert.Equal(0, this.folders.SaveCount);
    }

    /// <summary>A folder the source never sent anything from has no local folder, and no server listing is waited on to learn it.</summary>
    [Fact]
    public async Task ResolveUnsynchronizedDestinationAsync_AnyMapping_IsUnmappedWithoutListingTheServer()
    {
        // Arrange
        var composition = this.Composition();
        var mapping = MailFolderMapping.ToRemotePath(MailFolderAlias.Create("Junk"), RemoteFolderPath.Create("Junk"));

        // Act
        var resolution = await composition.Held.ResolveUnsynchronizedDestinationAsync(Account, mapping, Token);

        // Assert
        Assert.Equal(MailboxDestinationOutcome.Unmapped, resolution.Outcome);
        await composition.RemoteFolderCatalog.DidNotReceiveWithAnyArgs().ListFoldersAsync(default!, default!, Token);
    }

    /// <summary>A held account's folders are the truth, so reading them offers every act.</summary>
    [Fact]
    public async Task DescribeFoldersAsync_AHeldAccount_OffersCreation()
    {
        // Act
        var management = await this.Composition().Held.DescribeFoldersAsync(Account, Token);

        // Assert
        Assert.NotNull(management);
        Assert.Contains(MailFolderAct.Create, management.AllowedActs);
    }

    /// <summary>No copy of a send is kept on a drained server, so the passes keeping those copies in step have nothing to do.</summary>
    [Fact]
    public async Task MirrorWaitingSendsAsync_AHeldAccount_AppendsNothing()
    {
        // Arrange
        var composition = this.Composition();

        // Act
        var results = await composition.Held.MirrorWaitingSendsAsync(Account, Token);

        // Assert
        Assert.Empty(results);
        await composition.OutgoingEmails.DidNotReceiveWithAnyArgs().ReadOutstandingAsync(default!, default, Token);
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
