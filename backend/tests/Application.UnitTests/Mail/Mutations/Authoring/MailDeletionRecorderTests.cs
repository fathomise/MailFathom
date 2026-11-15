// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Authoring;
using MailFathom.Application.Mail.Mutations.Authoring.Failures;
using MailFathom.Application.Mail.Mutations.Authoring.Idempotency;
using MailFathom.Application.Mail.Mutations.Local;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Mutations.Authoring;

/// <summary>Covers the deleting grant, the visibility rule the message is judged by, and the record a delete is written down as.</summary>
/// <remarks>
/// It is the shorter half of <see cref="MailRelocationRecorderTests" />, and shorter for one reason: a delete names no
/// folder, so nothing here is about a destination. What is left is who may ask, which mail this caller may ask about,
/// and what the account says becomes of the local copy.
/// </remarks>
public sealed class MailDeletionRecorderTests
{
    private static readonly MailFolderId StoredIn = MailFolderId.Create(Guid.Parse("0199c1a2-0000-7000-8000-0000000000e5"));

    private static readonly MailAccountId Account =
        MailAccountId.Create("personal");

    private static readonly MailFolderAlias Trash = MailFolderAlias.Create("Trash");

    private static readonly MailFolderAlias Withheld = MailFolderAlias.Create("Private");

    private static readonly StoredEmailId LocalEmail = StoredEmailId.Create(Guid.CreateVersion7());

    private static readonly MailboxMutationRequester Requester = MailboxMutationRequester.Command("call-1");

    private static readonly DateTimeOffset RecordedAt = new(2026, 9, 9, 9, 0, 0, TimeSpan.Zero);

    private static readonly MailActIdempotencyKey DeleteKey =
        MailActIdempotencyKey.Create("call-1", "deletes", [$"{LocalEmail.Value:N}"]);

    private readonly InMemoryMailboxMutationRecordStore records = new();

    private readonly InMemoryMailDraftStore drafts = new();

    private readonly IAuthoredDeleteEmailDispositionReader dispositions =
        Substitute.For<IAuthoredDeleteEmailDispositionReader>();

    /// <summary>Arranges the disposition every test shares, so a test that is about it overrides it afterwards.</summary>
    public MailDeletionRecorderTests() => this.dispositions
        .GetAuthoredDeleteDisposition(Arg.Any<MailAccountId>())
        .Returns(AuthoredDeleteEmailDisposition.RetainTombstone);

    /// <summary>The record names the occurrence a command will be issued against, and names no folder in either direction.</summary>
    [Fact]
    public async Task RecordAsync_ADeleteOfReadableMail_OpensOneRecordAgainstTheOccurrence()
    {
        // Arrange
        var target = TargetIn(Trash);
        var recorder = this.Recorder(target);

        // Act
        var result = await recorder.RecordAsync(LocalEmail, Requester, withdrawalWindow: null, MailboxReaderKind.Agent, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailDeletionOutcome.Recorded, result.Outcome);
        Assert.Equal(MailboxMutationLifecycle.Pending, result.Lifecycle);

        var request = Assert.Single(this.records.OpenedRequests);

        Assert.Equal(MailboxMutation.Delete, request.Mutation);
        Assert.Equal(target.Occurrence, request.Occurrence);
        Assert.Null(request.DestinationPath);
    }

    /// <summary>
    /// The local copy follows the account's configured posture, and it is written onto the record while the
    /// configuration still says it rather than read again in the pass that carries the delete out.
    /// </summary>
    [Theory]
    [InlineData(AuthoredDeleteEmailDisposition.RetainTombstone)]
    [InlineData(AuthoredDeleteEmailDisposition.EraseLocalCopy)]
    public async Task RecordAsync_ADeleteOfReadableMail_RecordsTheAccountsConfiguredDisposition(
        AuthoredDeleteEmailDisposition configured)
    {
        // Arrange
        this.dispositions.GetAuthoredDeleteDisposition(Account).Returns(configured);
        var recorder = this.Recorder(TargetIn(Trash));

        // Act
        await recorder.RecordAsync(LocalEmail, Requester, withdrawalWindow: null, MailboxReaderKind.Agent, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(configured, Assert.Single(this.records.OpenedRequests).LocalDisposition);
    }

    /// <summary>
    /// What the delete does on the server is read once, as it is recorded, and travels on the record, so a delete still
    /// in flight is performed the way it was asked for even when the setting has changed since.
    /// </summary>
    [Fact]
    public async Task RecordAsync_TheSettingChangesAfterTheDeleteIsRecorded_KeepsTheServerDispositionItWasRecordedUnder()
    {
        // Arrange
        this.dispositions.GetAuthoredDeleteServerDisposition(Account).Returns(AuthoredDeleteServerDisposition.FlagDeleted);
        var recorder = this.Recorder(TargetIn(Trash));

        // Act
        await recorder.RecordAsync(LocalEmail, Requester, withdrawalWindow: null, MailboxReaderKind.Agent, TestContext.Current.CancellationToken);
        this.dispositions.GetAuthoredDeleteServerDisposition(Account).Returns(AuthoredDeleteServerDisposition.Expunge);

        // Assert
        Assert.Equal(
            AuthoredDeleteServerDisposition.FlagDeleted,
            Assert.Single(this.records.OpenedRequests).ServerDisposition);
    }

    /// <summary>
    /// A draft whose message is deleted is given up with it, so its author's next save cannot file it back.
    /// </summary>
    [Fact]
    public async Task RecordAsync_TheMessageADraftIsFiledAs_GivesTheDraftUp()
    {
        // Arrange
        var draftId = await this.FileDraftAsync();
        var recorder = this.Recorder(TargetIn(Trash));

        // Act
        await recorder.RecordAsync(LocalEmail, Requester, withdrawalWindow: null, MailboxReaderKind.Agent, TestContext.Current.CancellationToken);

        // Assert
        var draft = await this.drafts.FindAsync(draftId, TestContext.Current.CancellationToken);

        Assert.NotNull(draft);
        Assert.True(draft.IsDiscarded);
        Assert.Null(draft.FiledEmail);
    }

    /// <summary>
    /// A draft's message is listed before any occurrence is bound to it, so its author deleting it gives the draft up
    /// through the account's custodian, which erases the message and removes the record, and answers that the act is
    /// finished with nothing to follow. Answering that the message is not there would put the row back in front of them.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(MailAccountCustodyPhase.Held)]
    [InlineData(MailAccountCustodyPhase.Restoring)]
    public async Task RecordAsync_TheAuthorDeletingADraftsMessageThatHoldsNoOccurrence_GivesTheDraftUpAndAnswersApplied(
        MailAccountCustodyPhase? phase)
    {
        // Arrange
        var draftId = await this.FileDraftAsync();
        var localFolders = phase is { } custody ? new InMemoryLocalMailFolderStore(Account, custody) : null;
        var recorder = this.Recorder(target: null, localFolders: localFolders);

        // Act
        var result = await recorder.RecordAsync(LocalEmail, Requester, withdrawalWindow: null, MailboxReaderKind.Person, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailDeletionOutcome.Applied, result.Outcome);
        Assert.Null(result.RecordId);
        Assert.Null(await this.drafts.FindAsync(draftId, TestContext.Current.CancellationToken));
        Assert.Equal(0, this.records.OpenedRecordCount);

        if (localFolders is not null)
        {
            Assert.Contains(localFolders.EraseRequests, erase => erase.Email == LocalEmail);
        }
    }

    /// <summary>
    /// Giving a draft up is its author's act, and an agent reads no unsent mail at all, so anybody else naming the
    /// message is told what they were told before and the draft stands.
    /// </summary>
    [Theory]
    [InlineData(MailboxReaderKind.Agent, false)]
    [InlineData(MailboxReaderKind.Person, true)]
    public async Task RecordAsync_SomebodyOtherThanItsAuthorDeletingADraftsMessageThatHoldsNoOccurrence_FindsNoMessageAndLeavesTheDraft(
        MailboxReaderKind reader,
        bool writtenByAnother)
    {
        // Arrange
        var draftId = await this.FileDraftAsync(writtenByAnother ? SyntheticUser.Another : SyntheticUser.Deployment);
        var recorder = this.Recorder(target: null);

        // Act
        var result = await recorder.RecordAsync(LocalEmail, Requester, withdrawalWindow: null, reader, TestContext.Current.CancellationToken);

        // Assert
        var draft = await this.drafts.FindAsync(draftId, TestContext.Current.CancellationToken);

        Assert.Equal(MailDeletionOutcome.MessageNotFound, result.Outcome);
        Assert.NotNull(draft);
        Assert.False(draft.IsDiscarded);
        Assert.Equal(LocalEmail, draft.FiledEmail);
    }

    /// <summary>
    /// The key is kept in the commit that gives the draft up, so a repeat arriving after the message was erased is
    /// answered as the first attempt was rather than as a message that is not there.
    /// </summary>
    [Fact]
    public async Task RecordAsync_ARepeatAfterADraftsMessageThatHeldNoOccurrenceWasDeleted_StillAnswersApplied()
    {
        // Arrange
        await this.FileDraftAsync();
        var keys = new InMemoryMailActKeyStore();
        var first = await this.Recorder(target: null, keys: keys)
            .RecordAsync(LocalEmail, Requester, withdrawalWindow: null, MailboxReaderKind.Person, DeleteKey, TestContext.Current.CancellationToken);

        // Act
        var repeat = await this.Recorder(target: null, keys: keys.AnotherReplica())
            .RecordAsync(LocalEmail, Requester, withdrawalWindow: null, MailboxReaderKind.Person, DeleteKey, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailDeletionOutcome.Applied, repeat.Outcome);
        Assert.Equal(first, repeat);
    }

    /// <summary>
    /// Deleting mail is its own grant, so a caller holding the one that files mail elsewhere is refused. Filing a
    /// message in the trash is reversible; this expunges the occurrence and leaves nothing to fetch back.
    /// </summary>
    [Fact]
    public async Task RecordAsync_ACallerHoldingOnlyTheMovingGrant_IsRefusedWithoutWritingAnything()
    {
        // Arrange
        var recorder = this.Recorder(
            TargetIn(Trash),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailMove));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            recorder.RecordAsync(LocalEmail, Requester, withdrawalWindow: null, MailboxReaderKind.Agent, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailDelete, refusal.RequiredPermission);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>A folder no agent may read is a folder no agent may delete mail out of, or the write surface would be the way round the withholding.</summary>
    [Fact]
    public async Task RecordAsync_AnEmailInAFolderWithheldFromAIAskedByAnAgent_ReportsTheMessageAsNotFound()
    {
        // Arrange
        var recorder = this.Recorder(TargetIn(Withheld));

        // Act
        var result = await recorder.RecordAsync(LocalEmail, Requester, withdrawalWindow: null, MailboxReaderKind.Agent, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailDeletionOutcome.MessageNotFound, result.Outcome);
        Assert.Null(result.RecordId);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>A folder withheld from AI is still the person's, so deleting its mail through the client is recorded like any other delete.</summary>
    [Fact]
    public async Task RecordAsync_AnEmailInAFolderWithheldFromAIAskedByThePerson_OpensTheRecord()
    {
        // Arrange
        var recorder = this.Recorder(TargetIn(Withheld));

        // Act
        var result = await recorder.RecordAsync(LocalEmail, Requester, withdrawalWindow: null, MailboxReaderKind.Person, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result.RecordId);
        Assert.Equal(1, this.records.OpenedRecordCount);
    }

    /// <summary>An email no row carries answers exactly as a withheld one does, so asking cannot reveal which identifiers exist.</summary>
    [Fact]
    public async Task RecordAsync_AnEmailThisDeploymentHoldsNoRowFor_ReportsTheMessageAsNotFound()
    {
        // Arrange
        var recorder = this.Recorder(target: null);

        // Act
        var result = await recorder.RecordAsync(LocalEmail, Requester, withdrawalWindow: null, MailboxReaderKind.Agent, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailDeletionOutcome.MessageNotFound, result.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>
    /// An account removed from configuration between the read and the write leaves the disposition unanswerable, which
    /// is this caller's message to report rather than a fault that ends the batch.
    /// </summary>
    [Fact]
    public async Task RecordAsync_AnAccountThatLeftConfigurationMidRequest_ReportsItRatherThanFailing()
    {
        // Arrange
        this.dispositions
            .GetAuthoredDeleteDisposition(Account)
            .Throws(new InvalidOperationException("No account carries the identifier personal."));
        var recorder = this.Recorder(TargetIn(Trash));

        // Act
        var result = await recorder.RecordAsync(LocalEmail, Requester, withdrawalWindow: null, MailboxReaderKind.Agent, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailDeletionOutcome.AccountNoLongerConfigured, result.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>A retry under one identity is one delete, because the record store admits one per occurrence, requester, and mutation.</summary>
    [Fact]
    public async Task RecordAsync_TheSameDeleteAskedTwiceUnderOneRequester_OpensOneRecord()
    {
        // Arrange
        var recorder = this.Recorder(TargetIn(Trash));

        // Act
        var first = await recorder.RecordAsync(LocalEmail, Requester, withdrawalWindow: null, MailboxReaderKind.Agent, TestContext.Current.CancellationToken);
        var second = await recorder.RecordAsync(LocalEmail, Requester, withdrawalWindow: null, MailboxReaderKind.Agent, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, this.records.OpenedRecordCount);
        Assert.Equal(first.RecordId, second.RecordId);
    }

    /// <summary>Which folder the message is in decides nothing here, the client's rule being a sentence on a screen rather than a policy of the deployment's.</summary>
    [Fact]
    public async Task RecordAsync_AnEmailOutsideTheTrash_IsRecordedLikeAnyOther()
    {
        // Arrange
        var recorder = this.Recorder(TargetIn(MailFolderAlias.Create("INBOX")));

        // Act
        var result = await recorder.RecordAsync(LocalEmail, Requester, withdrawalWindow: null, MailboxReaderKind.Agent, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailDeletionOutcome.Recorded, result.Outcome);
        Assert.Equal(MailboxMutation.Delete, Assert.Single(this.records.OpenedRequests).Mutation);
    }

    /// <summary>A delete waits on the convergence pass exactly as a move does, so it must not sit there until the interval is out.</summary>
    [Fact]
    public async Task RecordAsync_ADelete_BringsTheAccountsNextSynchronizationRunForward()
    {
        // Arrange
        var runSignal = new MailAccountRunSignal();
        var recorder = this.Recorder(TargetIn(Trash), runSignal: runSignal);

        // Act
        await recorder.RecordAsync(LocalEmail, Requester, withdrawalWindow: null, MailboxReaderKind.Agent, TestContext.Current.CancellationToken);

        // Assert
        using var waiting = runSignal.Register(Account, TestContext.Current.CancellationToken);

        Assert.True(waiting.Token.IsCancellationRequested);
    }

    /// <summary>
    /// A delete a person may still take back is written down held, and the account is deliberately left asleep: a pass
    /// brought forward now would issue the command while the way back is still on their screen.
    /// </summary>
    [Fact]
    public async Task RecordAsync_ADeleteCarryingAWithdrawalWindow_HoldsTheRecordAndLeavesTheAccountAsleep()
    {
        // Arrange
        var runSignal = new MailAccountRunSignal();
        var recorder = this.Recorder(TargetIn(Trash), runSignal: runSignal);

        // Act
        await recorder.RecordAsync(
            LocalEmail,
            Requester,
            TimeSpan.FromSeconds(15),
            MailboxReaderKind.Agent,
            TestContext.Current.CancellationToken);

        // Assert
        using var waiting = runSignal.Register(Account, TestContext.Current.CancellationToken);

        Assert.False(waiting.Token.IsCancellationRequested);
        Assert.Equal(
            RecordedAt + TimeSpan.FromSeconds(15),
            this.records.HeldUntilOf(Assert.Single(this.records.OpenedRequests)));
    }

    /// <summary>A caller naming no window is every caller but the client's own, and its delete waits for nothing.</summary>
    [Fact]
    public async Task RecordAsync_ADeleteCarryingNoWithdrawalWindow_HoldsTheRecordForNoTime()
    {
        // Arrange
        var recorder = this.Recorder(TargetIn(Trash));

        // Act
        await recorder.RecordAsync(LocalEmail, Requester, withdrawalWindow: null, MailboxReaderKind.Agent, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(this.records.HeldUntilOf(Assert.Single(this.records.OpenedRequests)));
    }

    /// <summary>
    /// A zero window is no wait at all, so it is written down as the unheld record it is and brings the account forward
    /// like one — rather than held until the instant it was written, which a pass would find due and nothing would wake.
    /// </summary>
    [Fact]
    public async Task RecordAsync_ADeleteCarryingAZeroWithdrawalWindow_IsRecordedUnheldAndBringsTheRunForward()
    {
        // Arrange
        var runSignal = new MailAccountRunSignal();
        var recorder = this.Recorder(TargetIn(Trash), runSignal: runSignal);

        // Act
        await recorder.RecordAsync(LocalEmail, Requester, TimeSpan.Zero, MailboxReaderKind.Agent, TestContext.Current.CancellationToken);

        // Assert
        using var waiting = runSignal.Register(Account, TestContext.Current.CancellationToken);

        Assert.True(waiting.Token.IsCancellationRequested);
        Assert.Null(this.records.HeldUntilOf(Assert.Single(this.records.OpenedRequests)));
    }

    /// <summary>
    /// A restoring account's delete is applied at once and still owes the source a record, so the account is woken for
    /// it exactly as a mirrored account's delete wakes it — otherwise the source waits out the ordinary schedule for a
    /// change the person has already seen take effect.
    /// </summary>
    [Fact]
    public async Task RecordAsync_ADeleteOnARestoringAccountCarryingNoWindow_AppliesItAndBringsTheRunForward()
    {
        // Arrange
        var runSignal = new MailAccountRunSignal();
        var recorder = this.RestoringRecorder(runSignal);

        // Act
        var result = await recorder.RecordAsync(LocalEmail, Requester, withdrawalWindow: null, MailboxReaderKind.Agent, TestContext.Current.CancellationToken);

        // Assert
        using var waiting = runSignal.Register(Account, TestContext.Current.CancellationToken);

        Assert.Equal(MailDeletionOutcome.Applied, result.Outcome);
        Assert.NotNull(result.RecordId);
        Assert.True(waiting.Token.IsCancellationRequested);
    }

    /// <summary>
    /// A window asked for on a restoring account buys nothing, because the message has already moved into the local
    /// trash: the record the source is owed carries no hold and the run is woken for it at once.
    /// </summary>
    [Fact]
    public async Task RecordAsync_ADeleteOnARestoringAccountCarryingAWindow_AppliesItAndStillOwesTheSourceAtOnce()
    {
        // Arrange
        var runSignal = new MailAccountRunSignal();
        var recorder = this.RestoringRecorder(runSignal);

        // Act
        var result = await recorder.RecordAsync(
            LocalEmail,
            Requester,
            TimeSpan.FromSeconds(15),
            MailboxReaderKind.Agent,
            TestContext.Current.CancellationToken);

        // Assert
        using var waiting = runSignal.Register(Account, TestContext.Current.CancellationToken);

        Assert.Equal(MailDeletionOutcome.Applied, result.Outcome);
        Assert.NotNull(result.RecordId);
        Assert.True(waiting.Token.IsCancellationRequested);
        Assert.Null(this.records.HeldUntilOf(Assert.Single(this.records.OpenedRequests)));
    }

    /// <summary>
    /// The grant is asked before the window is measured, as a withdrawal asks it before it measures a batch, so a caller
    /// holding nothing is refused as unauthorized whatever else is wrong with what it sent.
    /// </summary>
    [Fact]
    public async Task RecordAsync_ACallerWithoutTheGrantNamingANegativeWindow_IsRefusedAsUnauthorized()
    {
        // Arrange
        var recorder = this.Recorder(
            TargetIn(Trash),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailFlagsWrite));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() => recorder.RecordAsync(
            LocalEmail,
            Requester,
            TimeSpan.FromSeconds(-1),
            MailboxReaderKind.Agent,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailDelete, refusal.RequiredPermission);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>A repeat under the act's key, reaching another replica, answers the first delete's record and opens no second one.</summary>
    [Fact]
    public async Task RecordAsync_ARepeatUnderTheSameKey_AnswersTheFirstOutcomeAndRecordsNothingAgain()
    {
        // Arrange
        var keys = new InMemoryMailActKeyStore();
        var first = await this.Recorder(TargetIn(Trash), keys: keys)
            .RecordAsync(LocalEmail, Requester, withdrawalWindow: null, MailboxReaderKind.Person, DeleteKey, TestContext.Current.CancellationToken);

        // Act
        var repeat = await this.Recorder(TargetIn(Trash), keys: keys.AnotherReplica())
            .RecordAsync(LocalEmail, Requester, withdrawalWindow: null, MailboxReaderKind.Person, DeleteKey, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(first, repeat);
        Assert.Equal(1, this.records.OpenedRecordCount);
    }

    /// <summary>
    /// The key is asked for before the email is looked up, so a repeat arriving after the delete erased the message is
    /// still answered with the first outcome rather than as a message that is not there.
    /// </summary>
    [Fact]
    public async Task RecordAsync_ARepeatAfterTheStoredEmailIsGone_StillAnswersTheFirstOutcome()
    {
        // Arrange
        var keys = new InMemoryMailActKeyStore();
        var first = await this.Recorder(TargetIn(Trash), keys: keys)
            .RecordAsync(LocalEmail, Requester, withdrawalWindow: null, MailboxReaderKind.Person, DeleteKey, TestContext.Current.CancellationToken);

        // Act
        var repeat = await this.Recorder(target: null, keys: keys.AnotherReplica())
            .RecordAsync(LocalEmail, Requester, withdrawalWindow: null, MailboxReaderKind.Person, DeleteKey, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailDeletionOutcome.Recorded, repeat.Outcome);
        Assert.Equal(first, repeat);
    }

    /// <summary>A key the person already used for another act is refused rather than answered with that act's outcome.</summary>
    [Fact]
    public async Task RecordAsync_AKeyAlreadyUsedForADifferentAct_IsRefusedAndRecordsNothing()
    {
        // Arrange
        var keys = new InMemoryMailActKeyStore();
        await this.Recorder(TargetIn(Trash), keys: keys)
            .RecordAsync(LocalEmail, Requester, withdrawalWindow: null, MailboxReaderKind.Person, DeleteKey, TestContext.Current.CancellationToken);
        var otherAct = MailActIdempotencyKey.Create("call-1", "moves", [$"{LocalEmail.Value:N}|archive"]);

        // Act
        var refusal = await Assert.ThrowsAsync<MailFlagChangeInvalidException>(() => this.Recorder(TargetIn(Trash), keys: keys.AnotherReplica())
            .RecordAsync(LocalEmail, Requester, withdrawalWindow: null, MailboxReaderKind.Person, otherAct, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFlagChangeInvalidException.RequestIdReusedForAnotherAct().Message, refusal.Message);
        Assert.Equal(1, this.records.OpenedRecordCount);
    }

    private MailDeletionRecorder Recorder(
        AuthoredMailboxTarget? target,
        AccessAuthorization? authorization = null,
        MailAccountRunSignal? runSignal = null,
        InMemoryLocalMailFolderStore? localFolders = null,
        InMemoryLocalEmailStateStore? states = null,
        IMailActKeyStore? keys = null)
    {
        var callerAuthorization =
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailDelete);
        var accountCatalog = AssignedMailAccountCatalogs.For(callerAuthorization, SyntheticServedAccount.Of(Account));

        var targets = Substitute.For<IAuthoredMailboxTargetReader>();
        targets.FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(target));

        var sessions = Substitute.For<IPersistenceSessionFactory>();
        sessions.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        var participation = StubMailFolderParticipation
            .Mapping(
                new MailFolderIdentity(Account, Trash),
                new MailFolderIdentity(Account, MailFolderAlias.Create("INBOX")))
            .Hiding(new MailFolderIdentity(Account, Withheld));

        return new MailDeletionRecorder(
            callerAuthorization,
            new MailboxScopeResolver(
                accountCatalog,
                participation,
                StubDeploymentMailFolders.Of(participation),
                StubMailFolderMappings.ResolvingNothing),
            targets,
            this.dispositions,
            MailboxChangeSubmissions.Over(this.records, localFolders, states),
            new OptimisticConcurrencyRetryPolicy(
                sessions,
                new PersistenceConcurrencyOptions(),
                new FakeTimeProvider(RecordedAt)),
            runSignal ?? new MailAccountRunSignal(),
            new FakeTimeProvider(RecordedAt),
            new MailActIdempotency(callerAuthorization, keys ?? Substitute.For<IMailActKeyStore>(), new FakeTimeProvider(RecordedAt)),
            this.drafts,
            this.DraftFiler(sessions, localFolders));
    }

    /// <summary>Builds what settles a draft given up, over the drafts these tests write and the custody the account is in.</summary>
    private MailDraftFiler DraftFiler(IPersistenceSessionFactory sessions, InMemoryLocalMailFolderStore? localFolders)
    {
        var defaults = new MailboxCustodianComposition(new FakeTimeProvider(RecordedAt));

        return new MailboxCustodianComposition(defaults.Clock)
        {
            Drafts = this.drafts,
            PersistenceSessions = sessions,
            LocalFolders = localFolders ?? defaults.LocalFolders,
            Custody = localFolders?.Custody ?? defaults.Custody,
        }.DraftFiler;
    }

    /// <summary>Writes down a draft whose current revision the folder shows as the message these tests act on.</summary>
    /// <param name="writtenBy">The draft's author, defaulting to the person these tests act as.</param>
    private async Task<MailDraftId> FileDraftAsync(UserId? writtenBy = null)
    {
        var session = Substitute.For<IPersistenceSession>();

        var draft = await this.drafts.OpenAsync(
            session,
            Account,
            writtenBy ?? SyntheticUser.Deployment,
            OutgoingEmailRequester.Command("mfctl-draft"),
            [],
            "A draft",
            OutgoingReceiptRequests.None,
            mimeByteLength: 64,
            RecordedAt,
            TestContext.Current.CancellationToken);

        await this.drafts.RecordFiledAsync(session, draft.Id, LocalEmail, draft.Revision, TestContext.Current.CancellationToken);

        return draft.Id;
    }

    /// <summary>Arranges a restoring account whose message still stands on the source and is not yet in the local trash.</summary>
    private MailDeletionRecorder RestoringRecorder(MailAccountRunSignal runSignal)
    {
        var target = TargetIn(MailFolderAlias.Create("INBOX"));
        var states = new InMemoryLocalEmailStateStore(Account);

        states.Store(
            LocalEmail,
            new LocalEmailState(
                target.Folder,
                HoldsSourceOccurrence: true,
                Folder: null,
                IsSeen: false,
                IsFlagged: false,
                EmailKeywords.Create([])));

        return this.Recorder(
            target,
            runSignal: runSignal,
            localFolders: new InMemoryLocalMailFolderStore(Account, MailAccountCustodyPhase.Restoring),
            states: states);
    }

    private static AuthoredMailboxTarget TargetIn(MailFolderAlias folderAlias)
    {
        var folder = MailFolderResolution.FirstBindingOf(folderAlias, RemoteFolderPath.Create(folderAlias.Value));

        return new AuthoredMailboxTarget(
            EmailOccurrenceId.Create(Account, folder.Id, ImapUidValidity.Create(42), ImapUid.Create(7)),
            folder,
            StoredIn);
    }

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
