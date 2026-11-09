// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Accounts.Custody;
using MailFathom.Application.Coordination;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Mail.Mutations.Local;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Accounts.Custody;

/// <summary>Covers how an account whose source is the truth carries its acts: through the source, committed locally first where every replica and the server allow it.</summary>
public sealed class MirroredMailboxCustodianTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("primary");

    private static readonly MailFolderResolution Inbox =
        MailFolderResolution.FirstBindingOf(MailFolderAlias.Create("INBOX"), RemoteFolderPath.Create("INBOX"));

    private static readonly MailFolderResolution Archive =
        MailFolderResolution.FirstBindingOf(MailFolderAlias.Create("Archive"), RemoteFolderPath.Create("Archive"));

    private static readonly MailFolderId ArchiveFolder = MailFolderId.Create(Guid.Parse("0199c1a2-0000-7000-8000-0000000000f1"));

    private static readonly StoredEmailId Email = StoredEmailId.Create(Guid.Parse("0199c1a2-0000-7000-8000-0000000000a1"));

    private static readonly EmailOccurrenceId Occurrence =
        EmailOccurrenceId.Create(Account, Inbox.Id, ImapUidValidity.Create(7), ImapUid.Create(42));

    private static readonly MailboxMutationRequester Requester = MailboxMutationRequester.Command("call-1");

    private readonly InMemoryMailboxMutationRecordStore records = new();

    private readonly InMemoryLocalEmailStateStore states = new(Account);

    private readonly IPersistenceSession session = Substitute.For<IPersistenceSession>();

    private readonly InMemoryLocalMailFolderStore folders = new(Account, MailAccountCustodyPhase.Mirrored);

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

    /// <summary>
    /// Once every replica knows the overlay, a change on a message is committed to stored state beside its record, and
    /// the record keeps what the server was last seen holding so synchronization leaves the field alone.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_AFlagChangeOnceEveryReplicaKnowsTheOverlay_CommitsItLocallyBesideItsRecord()
    {
        // Arrange
        this.StoreUnread();

        // Act
        var submitted = await this.LocalFirstCustodian().SubmitAsync(this.session, Change(SeenRequest()), Token);

        // Assert
        Assert.Equal(MailboxChangeSubmissionOutcome.Applied, submitted.Outcome);
        Assert.True(this.states.States[Email].IsSeen);
        Assert.Equal(MailboxMutationLocalChange.AlreadyCommitted, submitted.Record!.LocalChange);
        Assert.False(submitted.Record.Overlay!.ObservedSeenState);
    }

    /// <summary>
    /// During a rolling upgrade an older replica's synchronization would write over a field left outstanding, so a
    /// change is recorded alone while any live lease names a build that does not know the overlay.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_WhileAnOlderBuildHoldsALease_RecordsTheChangeAlone()
    {
        // Arrange
        this.StoreUnread();
        var custodian = this.Composition(
            MailboxCustodianComposition.LeasesHeldBy(WorkLeaseHolder.ForBuild("0.9.0"), WorkLeaseHolder.NewHold()),
            capabilities: null).Mirrored;

        // Act
        var submitted = await custodian.SubmitAsync(this.session, Change(SeenRequest()), Token);

        // Assert
        Assert.Equal(MailboxChangeSubmissionOutcome.Recorded, submitted.Outcome);
        Assert.False(this.states.States[Email].IsSeen);
    }

    /// <summary>
    /// A later change to the same field withdraws the earlier one that was never issued, and keeps the earlier one's
    /// reading of the server, since that is still what the server holds.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_ReadThenUnread_WithdrawsTheReadAndKeepsTheServersOriginalState()
    {
        // Arrange
        this.StoreUnread();
        var custodian = this.LocalFirstCustodian();
        var read = SeenRequest();
        var unread = MailboxMutationRequest.SetSeen(Email, Occurrence, MailboxMutationRequester.Command("call-2"), false);
        await custodian.SubmitAsync(this.session, Change(read), Token);

        // Act
        var submitted = await custodian.SubmitAsync(this.session, Change(unread), Token);

        // Assert
        Assert.Equal(MailboxMutationStage.Cancelled, this.records.RecordOf(read).Stage);
        Assert.False(this.states.States[Email].IsSeen);
        Assert.False(submitted.Record!.Overlay!.ObservedSeenState);
    }

    /// <summary>A request already recorded answers with its record, and the state it committed is not written twice.</summary>
    [Fact]
    public async Task SubmitAsync_TheSameRequestTwice_OpensOneRecord()
    {
        // Arrange
        this.StoreUnread();
        var custodian = this.LocalFirstCustodian();
        var first = await custodian.SubmitAsync(this.session, Change(SeenRequest()), Token);

        // Act
        var second = await custodian.SubmitAsync(this.session, Change(SeenRequest()), Token);

        // Assert
        Assert.Equal(first.Record!.Id, second.Record!.Id);
        Assert.Equal(1, this.records.OpenedRecordCount);
    }

    /// <summary>A delete is committed by its record alone, which hides the message; its stored state is left as it was.</summary>
    [Fact]
    public async Task SubmitAsync_ADeleteOnAServerThatExpungesByUid_IsAppliedThroughItsRecord()
    {
        // Arrange
        this.StoreUnread();

        // Act
        var submitted = await this.LocalFirstCustodian().SubmitAsync(this.session, Change(DeleteRequest()), Token);

        // Assert
        Assert.Equal(MailboxChangeSubmissionOutcome.Applied, submitted.Outcome);
        Assert.NotNull(submitted.Record!.Overlay);
        Assert.Equal(MailboxMutationLocalChange.AlreadyCommitted, submitted.Record.LocalChange);
    }

    /// <summary>A delete the server could only expunge with everything else flagged waits for the server rather than vanishing and coming back.</summary>
    [Fact]
    public async Task SubmitAsync_ADeleteOnAServerThatCannotExpungeByUid_IsRecordedAlone()
    {
        // Arrange
        this.StoreUnread();

        // Act
        var submitted = await this.LocalFirstCustodian(MailboxServerCapabilities.None)
            .SubmitAsync(this.session, Change(DeleteRequest()), Token);

        // Assert
        Assert.Equal(MailboxChangeSubmissionOutcome.Recorded, submitted.Outcome);
        Assert.Null(submitted.Record!.Overlay);
    }

    /// <summary>A move creates an occurrence the server has to name, so it waits for the server where none was seen advertising how.</summary>
    [Fact]
    public async Task SubmitAsync_AMoveOnAServerThatNamesNoNewOccurrence_IsRecordedAlone()
    {
        // Arrange
        this.StoreUnread();
        var move = MoveToArchive();

        // Act
        var submitted = await this.LocalFirstCustodian(MailboxServerCapabilities.None).SubmitAsync(this.session, move, Token);

        // Assert
        Assert.Equal(MailboxChangeSubmissionOutcome.Recorded, submitted.Outcome);
        Assert.Null(this.states.States[Email].Folder);
    }

    /// <summary>Where the server was seen advertising UIDPLUS, a move puts the message in its local folder at once.</summary>
    [Fact]
    public async Task SubmitAsync_AMoveOnAServerThatNamesTheNewOccurrence_PutsTheMessageInItsFolderAtOnce()
    {
        // Arrange
        this.StoreUnread();

        // Act
        var submitted = await this.LocalFirstCustodian(MailboxServerCapabilities.UidPlus)
            .SubmitAsync(this.session, MoveToArchive(), Token);

        // Assert
        Assert.Equal(MailboxChangeSubmissionOutcome.Applied, submitted.Outcome);
        Assert.Equal(ArchiveFolder, this.states.States[Email].Folder);
        Assert.Null(submitted.Record!.Overlay!.ObservedFolder);
    }

    /// <summary>A person moving a message into the folder a local commit already put it in is told it is there.</summary>
    [Fact]
    public async Task SubmitAsync_APersonsMoveIntoTheFolderALocalCommitPutItIn_AnswersAlreadyThere()
    {
        // Arrange
        this.StoreUnread();
        var custodian = this.LocalFirstCustodian(MailboxServerCapabilities.Imap4Rev2);
        await custodian.SubmitAsync(this.session, MoveToArchive(), Token);
        var again = MoveToArchive() with
        {
            Request = MailboxMutationRequest.Relocate(Email, Occurrence, MailboxMutationRequester.Command("call-2"), Archive.RemotePath),
        };

        // Act
        var submitted = await custodian.SubmitAsync(this.session, again, Token);

        // Assert
        Assert.Equal(MailboxChangeSubmissionOutcome.AlreadyInDestination, submitted.Outcome);
        Assert.Equal(1, this.records.OpenedRecordCount);
    }

    /// <summary>A replacement sends only what it changed against the stored keywords, so a keyword it did not touch stays out of every command.</summary>
    /// <param name="carried">What the stored message carries, comma-separated.</param>
    /// <param name="named">What the act names, comma-separated.</param>
    /// <param name="removal">What the removal should name, comma-separated, or empty where none is opened.</param>
    /// <param name="addition">What the addition should name, comma-separated, or empty where none is opened.</param>
    [Theory]
    [InlineData("a", "something1", "A", "something1")]
    [InlineData("a,b", "a,c", "B", "c")]
    [InlineData("a", "", "A", "")]
    [InlineData("", "a", "", "a")]
    public async Task SubmitAsync_AKeywordReplacementCommittedLocallyFirst_OpensOnlyTheRemovalAndTheAdditionItsDifferenceNeeds(
        string carried,
        string named,
        string removal,
        string addition)
    {
        // Arrange
        this.StoreKeywords(Words(carried));

        // Act
        var submitted = await this.LocalFirstCustodian().SubmitAsync(this.session, Change(Replacement("call-1", Words(named))), Token);

        // Assert
        Assert.Equal(MailboxChangeSubmissionOutcome.Applied, submitted.Outcome);
        Assert.Equal(EmailKeywords.Create(Words(named)).Values, this.states.States[Email].Keywords.Values);
        Assert.Equal(Words(removal), NamedBy(submitted, MailboxMutation.RemoveKeywords));
        Assert.Equal(Words(addition), NamedBy(submitted, MailboxMutation.AddKeywords));
        Assert.All(submitted.Records, record => Assert.Equal(EmailKeywords.Create(Words(carried)).Values, record.Overlay!.ObservedKeywords!.Values));
    }

    /// <summary>A replacement naming what the message already carries, in whatever spelling, has nothing to tell the server.</summary>
    [Fact]
    public async Task SubmitAsync_AKeywordReplacementWithNoDifference_OpensNoRecord()
    {
        // Arrange
        this.StoreKeywords("a", "b");

        // Act
        var submitted = await this.LocalFirstCustodian().SubmitAsync(this.session, Change(Replacement("call-1", "B", "a")), Token);

        // Assert
        Assert.Equal(MailboxChangeSubmissionOutcome.Applied, submitted.Outcome);
        Assert.Empty(submitted.Records);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>A keyword a server reported that no command can name is left where it is rather than failing the act.</summary>
    [Fact]
    public async Task SubmitAsync_AKeywordReplacementOverAKeywordThatIsNotAnAtom_LeavesItOutOfTheRemoval()
    {
        // Arrange
        this.StoreKeywords("a(b", "c");

        // Act
        var submitted = await this.LocalFirstCustodian().SubmitAsync(this.session, Change(Replacement("call-1")), Token);

        // Assert
        Assert.Equal(["C"], NamedBy(submitted, MailboxMutation.RemoveKeywords));
        Assert.Equal(["A(B"], this.states.States[Email].Keywords.Values);
    }

    /// <summary>While the stored row may not have moved, it is no base for a difference, so the replacement stays one exact record.</summary>
    [Fact]
    public async Task SubmitAsync_AKeywordReplacementWhileAnOlderBuildHoldsALease_RecordsTheReplacementAlone()
    {
        // Arrange
        this.StoreKeywords("a");
        var custodian = this.Composition(
            MailboxCustodianComposition.LeasesHeldBy(WorkLeaseHolder.ForBuild("0.9.0"), WorkLeaseHolder.NewHold()),
            capabilities: null).Mirrored;

        // Act
        var submitted = await custodian.SubmitAsync(this.session, Change(Replacement("call-1", "b")), Token);

        // Assert
        Assert.Equal(MailboxChangeSubmissionOutcome.Recorded, submitted.Outcome);
        Assert.Equal(MailboxMutation.SetKeywords, Assert.Single(submitted.Records).Request.Mutation);
        Assert.Equal(["A"], this.states.States[Email].Keywords.Values);
    }

    /// <summary>
    /// A run of quick replacements leaves whatever records survive superseding, and the server they reach ends on what
    /// the last one named, with a keyword another client set never touched.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_ARunOfQuickReplacements_ReachesTheServerAsWhatTheLastOneNamed()
    {
        // Arrange
        this.StoreKeywords("a");
        var custodian = this.LocalFirstCustodian();
        var server = new HashSet<string>(["A", "ANOTHER-CLIENT"], StringComparer.OrdinalIgnoreCase);

        // Act
        await custodian.SubmitAsync(this.session, Change(Replacement("call-1", "b")), Token);
        await custodian.SubmitAsync(this.session, Change(Replacement("call-2", "a", "c")), Token);
        await custodian.SubmitAsync(this.session, Change(Replacement("call-3", "c")), Token);

        var outstanding = await this.records.ReadOutstandingAsync(Account, limit: 100, Token);

        foreach (var request in outstanding.OrderBy(static candidate => candidate.Record.RecordedAt).Select(static candidate => candidate.Record.Request))
        {
            if (request.Mutation == MailboxMutation.AddKeywords)
            {
                server.UnionWith(request.Keywords!.Values);
            }
            else
            {
                server.ExceptWith(request.Keywords!.Values);
            }
        }

        // Assert
        Assert.Equal(["ANOTHER-CLIENT", "C"], server.Select(static keyword => keyword.ToUpperInvariant()).Order(StringComparer.Ordinal));
        Assert.Equal(["C"], this.states.States[Email].Keywords.Values);
    }

    private static string[] Words(string list) => list.Split(',', StringSplitOptions.RemoveEmptyEntries);

    private static IEnumerable<string> NamedBy(SubmittedMailboxChange submitted, MailboxMutation mutation) =>
        submitted.Records.Where(record => record.Request.Mutation == mutation).SelectMany(static record => record.Request.Keywords!.Values);

    private static MailboxMutationRequest Replacement(string requester, params string[] keywords) =>
        MailboxMutationRequest.SetKeywords(Email, Occurrence, MailboxMutationRequester.Command(requester), AuthoredMailKeywords.Create(keywords));

    private void StoreKeywords(params string[] keywords) => this.states.Store(
        Email,
        new LocalEmailState(Inbox, HoldsSourceOccurrence: true, Folder: null, IsSeen: false, IsFlagged: false, EmailKeywords.Create(keywords)));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static MailboxMutationRequest SeenRequest() => MailboxMutationRequest.SetSeen(Email, Occurrence, Requester, true);

    private static MailboxMutationRequest MoveRequest() =>
        MailboxMutationRequest.Relocate(Email, Occurrence, Requester, Inbox.RemotePath);

    private static MailboxChange Change(MailboxMutationRequest request) =>
        new(request, Destination: null, HeldUntil: null, PersonsMove: false, CrossesDraftsOrSentTreeOnTheServer: false, PreparedCopy: null);

    private static MailboxMutationRequest DeleteRequest() => MailboxMutationRequest.Delete(
        Email,
        Occurrence,
        Requester,
        AuthoredDeleteEmailDisposition.RetainTombstone,
        AuthoredDeleteServerDisposition.Expunge);

    private static MailboxChange MoveToArchive() =>
        Change(MailboxMutationRequest.Relocate(Email, Occurrence, Requester, Archive.RemotePath)) with
        {
            Destination = new MailboxDestination(Archive, IsMirrored: true),
            PersonsMove = true,
        };

    private MirroredMailboxCustodian LocalFirstCustodian(MailboxServerCapabilities advertised = MailboxServerCapabilities.UidPlus) =>
        this.Composition(
            MailboxCustodianComposition.LeasesHeldBy(WorkLeaseHolder.ForBuild("0.9.0")),
            MailboxServerCapabilityObservation.Observed(advertised, DateTimeOffset.UnixEpoch)).Mirrored;

    private MailboxCustodianComposition Composition(
        IWorkLeaseStore leases,
        MailboxServerCapabilityObservation? capabilities)
    {
        this.folders.Add(new LocalMailFolder(ArchiveFolder, ParentId: null, LocalMailFolderName.Create("Archive"), Role: null, Archive.Alias));

        return new MailboxCustodianComposition(new FakeTimeProvider())
        {
            Records = this.records,
            States = this.states,
            LocalFolders = this.folders,
            Leases = leases,
            Capabilities = MailboxCustodianComposition.CapabilitiesObserved(
                capabilities ?? MailboxServerCapabilityObservation.NeverObserved),
        };
    }

    private void StoreUnread() => this.states.Store(
        Email,
        new LocalEmailState(Inbox, HoldsSourceOccurrence: true, Folder: null, IsSeen: false, IsFlagged: false, EmailKeywords.Create([])));

    private MirroredMailboxCustodian Custodian() =>
        MailboxChangeSubmissions.Composing(this.records, states: this.states).Mirrored;
}
