// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Accounts.Custody;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Accounts.Custody;

/// <summary>Covers how an account whose source is the truth carries its acts: through the source, with nothing committed locally.</summary>
public sealed class MirroredMailboxCustodianTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("primary");

    private static readonly MailFolderResolution Inbox =
        MailFolderResolution.FirstBindingOf(MailFolderAlias.Create("INBOX"), RemoteFolderPath.Create("INBOX"));

    private static readonly StoredEmailId Email = StoredEmailId.Create(Guid.Parse("0199c1a2-0000-7000-8000-0000000000a1"));

    private static readonly EmailOccurrenceId Occurrence =
        EmailOccurrenceId.Create(Account, Inbox.Id, ImapUidValidity.Create(7), ImapUid.Create(42));

    private static readonly MailboxMutationRequester Requester = MailboxMutationRequester.Command("call-1");

    private readonly InMemoryMailboxMutationRecordStore records = new();

    private readonly InMemoryLocalEmailStateStore states = new(Account);

    private readonly IPersistenceSession session = Substitute.For<IPersistenceSession>();

    /// <summary>A change on a message is owed to the source, so it is written down for the converger and committed nowhere else.</summary>
    [Fact]
    public async Task SubmitAsync_AChangeOnAMessage_OpensTheRecordTheSourceIsOwedAndCommitsNothingLocally()
    {
        // Act
        var submitted = await this.Custodian().SubmitAsync(this.session, Change(SeenRequest()), Token);

        // Assert
        Assert.Equal(MailboxChangeSubmissionOutcome.Recorded, submitted.Outcome);
        Assert.Equal(MailboxMutationLocalChange.None, submitted.Record!.LocalChange);
        Assert.Empty(this.states.States);
    }

    /// <summary>A person moving a message into the folder it is in on the server is told so, and nothing is recorded.</summary>
    [Fact]
    public async Task SubmitAsync_APersonsMoveIntoTheFolderTheMessageIsIn_AnswersAlreadyThereWithoutARecord()
    {
        // Arrange
        var move = Change(MoveRequest()) with { Destination = new MailboxDestination(Inbox, IsMirrored: true), PersonsMove = true };

        // Act
        var submitted = await this.Custodian().SubmitAsync(this.session, move, Token);

        // Assert
        Assert.Equal(MailboxChangeSubmissionOutcome.AlreadyInDestination, submitted.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>The server's own drafts and sent tree decide what a person may move across, and a refusal records nothing.</summary>
    [Fact]
    public async Task SubmitAsync_APersonsMoveAcrossTheDraftsOrTheSentTree_IsRefusedWithoutARecord()
    {
        // Arrange
        var move = Change(MoveRequest()) with { PersonsMove = true, CrossesDraftsOrSentTreeOnTheServer = true };

        // Act
        var submitted = await this.Custodian().SubmitAsync(this.session, move, Token);

        // Assert
        Assert.Equal(MailboxChangeSubmissionOutcome.DestinationNotAllowed, submitted.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>A copy is a command for the source, so nothing is stored for one ahead of its transaction.</summary>
    [Fact]
    public async Task PrepareCopiesAsync_AnyMessage_PlacesNothing()
    {
        // Act
        var prepared = await this.Custodian().PrepareCopiesAsync(Account, [Email], Token);

        // Assert
        Assert.Empty(prepared.Of(Email));
    }

    /// <summary>A draft revision and a sent copy are appended to the source by the passes, so neither is prepared to be filed locally.</summary>
    [Fact]
    public async Task PrepareDraftCopyAsync_AnyRevision_PreparesNoLocalCopy()
    {
        // Act
        var copy = await this.Custodian().PrepareDraftCopyAsync(Account, "Subject: draft\r\n\r\nbody"u8.ToArray(), Token);

        // Assert
        Assert.Null(copy);
    }

    /// <summary>A folder act names the folder by the identity of its row, so an identity no row carries is a folder the account does not have.</summary>
    [Fact]
    public async Task RenameFolderAsync_AFolderNoRowNames_IsRefusedAsMissing()
    {
        // Arrange
        var custodian = new InMemoryLocalMailFolderStore(Account, MailAccountCustodyPhase.Mirrored)
            .Composing(new FakeTimeProvider())
            .Mirrored;

        // Act
        var outcome = await custodian.RenameFolderAsync(Account, MailFolderId.Mint(DateTimeOffset.UnixEpoch), "Plans", Token);

        // Assert
        Assert.Equal(MailFolderActRefusal.FolderMissing, outcome.Refusal);
    }

    /// <summary>An account with no row is not one this deployment serves, whatever the act.</summary>
    [Fact]
    public async Task CreateFolderAsync_AnAccountWithNoRow_IsRefusedAsMissing()
    {
        // Arrange
        var custodian = new InMemoryLocalMailFolderStore(MailAccountId.Create("other"), MailAccountCustodyPhase.Mirrored)
            .Composing(new FakeTimeProvider())
            .Mirrored;

        // Act
        var outcome = await custodian.CreateFolderAsync(Account, parent: null, "Plans", role: null, Token);

        // Assert
        Assert.Equal(MailFolderActRefusal.AccountMissing, outcome.Refusal);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static MailboxMutationRequest SeenRequest() => MailboxMutationRequest.SetSeen(Email, Occurrence, Requester, true);

    private static MailboxMutationRequest MoveRequest() =>
        MailboxMutationRequest.Relocate(Email, Occurrence, Requester, Inbox.RemotePath);

    private static MailboxChange Change(MailboxMutationRequest request) =>
        new(request, Destination: null, HeldUntil: null, PersonsMove: false, CrossesDraftsOrSentTreeOnTheServer: false, PreparedCopy: null);

    private MirroredMailboxCustodian Custodian() =>
        MailboxChangeSubmissions.Composing(this.records, states: this.states).Mirrored;
}
