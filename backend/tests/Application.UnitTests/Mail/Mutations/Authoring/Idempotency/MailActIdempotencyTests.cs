// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Mail.Mutations.Authoring;
using MailFathom.Application.Mail.Mutations.Authoring.Failures;
using MailFathom.Application.Mail.Mutations.Authoring.Idempotency;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
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

namespace MailFathom.Application.UnitTests.Mail.Mutations.Authoring.Idempotency;

/// <summary>Covers what a person's key makes of a repeated mailbox act: one outcome across replicas, and a refusal for a key reused for another act.</summary>
/// <remarks>
/// The flag recorder is what drives the shared service here; each other recorder's own tests cover its keyed path, the
/// outcome it rebuilds from a stored answer, and a key reused for another act. The unique-index race is stated through <see cref="InMemoryMailActKeyStore.CompetingWriter" />
/// and a session whose commit loses, since neither a database nor a second connection is available to a unit test.
/// </remarks>
public sealed class MailActIdempotencyTests
{
    private static readonly MailFolderId StoredIn = MailFolderId.Create(Guid.Parse("0199c1a2-0000-7000-8000-0000000000e5"));

    private static readonly MailAccountId Account = MailAccountId.Create("personal");

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    private static readonly StoredEmailId LocalEmail = StoredEmailId.Create(Guid.CreateVersion7());

    private static readonly MailboxMutationRequester Requester = MailboxMutationRequester.Command("call-1");

    private static readonly DateTimeOffset Now = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    private static readonly MailActIdempotencyKey Key =
        MailActIdempotencyKey.Create("call-1", "flags", [$"{LocalEmail.Value:N}|True"]);

    private static readonly AuthoredMailFlagChange MarkRead =
        AuthoredMailFlagChange.Create(LocalEmail, seen: true, flagged: null, null, null);

    /// <summary>The first attempt answers as it always did, and leaves the key with what it answered.</summary>
    [Fact]
    public async Task RecordAsync_TheFirstAttemptUnderAKey_KeepsTheKeyBesideTheChange()
    {
        // Arrange
        var keys = new InMemoryMailActKeyStore();
        var recorder = RecorderOver(new InMemoryMailboxMutationRecordStore(), TargetIn(Inbox), keys);

        // Act
        var result = await recorder.RecordAsync(MarkRead, Requester, MailboxReaderKind.Person, Key, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(result.Recorded);
        Assert.Equal(1, keys.Count);
    }

    /// <summary>A repeat after the change completed answers the first outcome and writes nothing, even under another requester that would have opened a record of its own.</summary>
    [Fact]
    public async Task RecordAsync_ARepeatAfterTheChangeCompleted_AnswersTheFirstOutcomeAndRecordsNothingAgain()
    {
        // Arrange
        var records = new InMemoryMailboxMutationRecordStore();
        var recorder = RecorderOver(records, TargetIn(Inbox), new InMemoryMailActKeyStore());
        var first = await recorder.RecordAsync(MarkRead, Requester, MailboxReaderKind.Person, Key, TestContext.Current.CancellationToken);

        // Act
        var repeat = await recorder.RecordAsync(
            MarkRead,
            MailboxMutationRequester.Command("another-requester"),
            MailboxReaderKind.Person,
            Key,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(first.Recorded, repeat.Recorded);
        Assert.Equal(1, records.OpenedRecordCount);
    }

    /// <summary>A repeat reaching another replica finds the row the first replica committed, because the table is the one place the key lives.</summary>
    [Fact]
    public async Task RecordAsync_ARepeatOnAnotherReplica_AnswersTheFirstOutcomeWithoutSubmittingAnything()
    {
        // Arrange
        var keys = new InMemoryMailActKeyStore();
        var first = await RecorderOver(new InMemoryMailboxMutationRecordStore(), TargetIn(Inbox), keys)
            .RecordAsync(MarkRead, Requester, MailboxReaderKind.Person, Key, TestContext.Current.CancellationToken);
        var recordsOnTheOtherReplica = new InMemoryMailboxMutationRecordStore();
        var otherReplica = RecorderOver(recordsOnTheOtherReplica, TargetIn(Inbox), keys.AnotherReplica());

        // Act
        var repeat = await otherReplica.RecordAsync(MarkRead, Requester, MailboxReaderKind.Person, Key, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(first.Recorded, repeat.Recorded);
        Assert.Equal((first.AccountId, first.Folder), (repeat.AccountId, repeat.Folder));
        Assert.Equal(0, recordsOnTheOtherReplica.OpenedRecordCount);
    }

    /// <summary>A repeat after the stored email and its record were deleted still answers the first outcome, which is why the key is not a column of the record.</summary>
    [Fact]
    public async Task RecordAsync_ARepeatAfterTheStoredEmailIsGone_StillAnswersTheFirstOutcome()
    {
        // Arrange
        var keys = new InMemoryMailActKeyStore();
        var first = await RecorderOver(new InMemoryMailboxMutationRecordStore(), TargetIn(Inbox), keys)
            .RecordAsync(MarkRead, Requester, MailboxReaderKind.Person, Key, TestContext.Current.CancellationToken);
        var afterDeletion = RecorderOver(new InMemoryMailboxMutationRecordStore(), target: null, keys.AnotherReplica());

        // Act
        var repeat = await afterDeletion.RecordAsync(MarkRead, Requester, MailboxReaderKind.Person, Key, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(first.Recorded, repeat.Recorded);
    }

    /// <summary>
    /// A repeat that passed its read while the first attempt was still running loses on the unique index and is run again
    /// from a fresh read, which finds the first attempt's row: one outcome, never an error and never a second change.
    /// </summary>
    [Fact]
    public async Task RecordAsync_ARepeatThatLosesTheRaceForTheKey_AnswersTheWinnersOutcomeOnTheRetry()
    {
        // Arrange
        var winnersRecord = new RecordedMailFlagMutation(
            MailboxMutation.SetSeen,
            MailboxMutationRecordId.Create(Guid.CreateVersion7()),
            MailboxMutationLifecycle.Pending);
        var winner = new InMemoryMailActKeyStore();
        var loser = winner.AnotherReplica();
        loser.CompetingWriter = () => winner.Seed(
            SyntheticUser.Deployment,
            Key,
            LocalEmail,
            new MailActOutcome("Recorded", [winnersRecord], [], Account.Value, StoredIn.ToString()),
            Now);
        var clock = new FakeTimeProvider(Now);
        var firstCommitObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var recorder = RecorderOver(
            new InMemoryMailboxMutationRecordStore(),
            TargetIn(Inbox),
            loser,
            clock,
            () => new OutcomeSession(
                ++attempts == 1 ? PersistenceCommitResult.ConcurrencyConflict : PersistenceCommitResult.Committed,
                attempts == 1 ? firstCommitObserved : null));

        // Act
        var recording = recorder.RecordAsync(MarkRead, Requester, MailboxReaderKind.Person, Key, TestContext.Current.CancellationToken);
        await firstCommitObserved.Task;
        var result = await AdvanceUntilCompletedAsync(clock, recording);

        // Assert
        Assert.Equal(2, attempts);
        Assert.Equal(winnersRecord, Assert.Single(result.Recorded));
    }

    /// <summary>A key reused for another act is refused rather than answered with the earlier act's outcome, and nothing is recorded for it.</summary>
    [Fact]
    public async Task RecordAsync_AKeyAlreadyUsedForADifferentAct_IsRefusedAndRecordsNothing()
    {
        // Arrange
        var keys = new InMemoryMailActKeyStore();
        await RecorderOver(new InMemoryMailboxMutationRecordStore(), TargetIn(Inbox), keys)
            .RecordAsync(MarkRead, Requester, MailboxReaderKind.Person, Key, TestContext.Current.CancellationToken);
        var records = new InMemoryMailboxMutationRecordStore();
        var otherAct = MailActIdempotencyKey.Create("call-1", "flags", [$"{LocalEmail.Value:N}|False"]);

        // Act
        var refusal = await Assert.ThrowsAsync<MailFlagChangeInvalidException>(() =>
            RecorderOver(records, TargetIn(Inbox), keys.AnotherReplica()).RecordAsync(
                AuthoredMailFlagChange.Create(LocalEmail, seen: false, flagged: null, null, null),
                Requester,
                MailboxReaderKind.Person,
                otherAct,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFlagChangeInvalidException.RequestIdReusedForAnotherAct().Message, refusal.Message);
        Assert.Equal(0, records.OpenedRecordCount);
        Assert.Equal(1, keys.Count);
    }

    /// <summary>The request-level check refuses a key whose first use asked for something else, whichever message that first use named.</summary>
    [Fact]
    public async Task RequireSameActAsync_AKeyFirstUsedForADifferentAct_IsRefused()
    {
        // Arrange
        var keys = new InMemoryMailActKeyStore();
        keys.Seed(
            SyntheticUser.Deployment,
            Key,
            StoredEmailId.Create(Guid.CreateVersion7()),
            new MailActOutcome("Applied", [], []),
            Now);
        var idempotency = IdempotencyOver(keys);

        // Act
        var sameAct = idempotency.RequireSameActAsync(Key, TestContext.Current.CancellationToken);
        var otherAct = Assert.ThrowsAsync<MailFlagChangeInvalidException>(() => idempotency.RequireSameActAsync(
            MailActIdempotencyKey.Create("call-1", "moves", [$"{LocalEmail.Value:N}|archive"]),
            TestContext.Current.CancellationToken));

        // Assert
        await sameAct;
        await otherAct;
    }

    /// <summary>One person's key never answers another's request, because the key is only ever read under the person who made the act.</summary>
    [Fact]
    public async Task FindAsync_ASecondPersonsRequestUnderTheSameKeyText_FindsNothing()
    {
        // Arrange
        var keys = new InMemoryMailActKeyStore();
        keys.Seed(SyntheticUser.Deployment, Key, LocalEmail, new MailActOutcome("Applied", [], []), Now);
        var otherPerson = new MailActIdempotency(
            AccessAuthorizations.ForUserGranted(
                UserId.Create(Guid.CreateVersion7()),
                MailFathomPermission.MailFlagsWrite),
            keys,
            new FakeTimeProvider(Now));

        // Act
        var found = await otherPerson.FindAsync(Key, LocalEmail, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(found);
    }

    /// <summary>A key older than its day is deleted by the sweep, and a younger one is kept for the repeat it may still answer.</summary>
    [Fact]
    public async Task EraseExpiredAsync_AKeyOlderThanTwentyFourHours_IsDeletedAndAYoungerOneKept()
    {
        // Arrange
        var keys = new InMemoryMailActKeyStore();
        var older = StoredEmailId.Create(Guid.CreateVersion7());
        var younger = StoredEmailId.Create(Guid.CreateVersion7());
        keys.Seed(SyntheticUser.Deployment, Key, older, new MailActOutcome("Applied", [], []), Now - TimeSpan.FromHours(24) - TimeSpan.FromMinutes(1));
        keys.Seed(SyntheticUser.Deployment, Key, younger, new MailActOutcome("Applied", [], []), Now - TimeSpan.FromHours(23));
        var retention = new MailActKeyRetention(keys, new FakeTimeProvider(Now));

        // Act
        var erased = await retention.EraseExpiredAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, erased);
        Assert.Null(await keys.FindAsync(SyntheticUser.Deployment, Key.Value, older, TestContext.Current.CancellationToken));
        Assert.NotNull(await keys.FindAsync(SyntheticUser.Deployment, Key.Value, younger, TestContext.Current.CancellationToken));
    }

    /// <summary>The window is the day the contract states, which the sweep's cutoff is measured from.</summary>
    [Fact]
    public void Window_IsTwentyFourHours() => Assert.Equal(TimeSpan.FromHours(24), MailActKeyRetention.Window);

    /// <summary>Moves the clock on until the recording completes, because the backoff's timer is created after the losing commit reports.</summary>
    /// <remarks>
    /// One advance can land before the retry policy has created its delay, and is then lost; advancing again on every turn
    /// reaches the timer whenever it is created, and the guard reports a recording that never completes as a failure
    /// rather than as a suite that stopped.
    /// </remarks>
    private static async Task<T> AdvanceUntilCompletedAsync<T>(FakeTimeProvider clock, Task<T> recording)
    {
        var guarded = recording.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        while (!guarded.IsCompleted)
        {
            clock.Advance(TimeSpan.FromSeconds(2));

            await Task.Yield();
        }

        return await guarded;
    }

    private static MailActIdempotency IdempotencyOver(InMemoryMailActKeyStore keys) => new(
        AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailFlagsWrite),
        keys,
        new FakeTimeProvider(Now));

    private static MailFlagChangeRecorder RecorderOver(
        InMemoryMailboxMutationRecordStore records,
        AuthoredMailboxTarget? target,
        InMemoryMailActKeyStore keys,
        FakeTimeProvider? clock = null,
        Func<IPersistenceSession>? sessionFactory = null)
    {
        var authorization = AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailFlagsWrite);
        var accountCatalog = AssignedMailAccountCatalogs.For(authorization, SyntheticServedAccount.Of(Account));

        var targets = Substitute.For<IAuthoredMailboxTargetReader>();
        targets.FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(target));

        var sessions = Substitute.For<IPersistenceSessionFactory>();
        sessions
            .BeginSessionAsync(Arg.Any<CancellationToken>())
            .Returns(_ => sessionFactory is null
                ? new OutcomeSession(PersistenceCommitResult.Committed)
                : sessionFactory());

        var participation = StubMailFolderParticipation.Mapping(new MailFolderIdentity(Account, Inbox));
        var time = clock ?? new FakeTimeProvider(Now);

        return new MailFlagChangeRecorder(
            authorization,
            new MailboxScopeResolver(
                accountCatalog,
                participation,
                StubDeploymentMailFolders.Of(participation),
                StubMailFolderMappings.ResolvingNothing),
            targets,
            MailboxChangeSubmissions.Over(records),
            new OptimisticConcurrencyRetryPolicy(sessions, new PersistenceConcurrencyOptions(), time),
            new MailAccountRunSignal(),
            new MailActIdempotency(authorization, keys, time));
    }

    private static AuthoredMailboxTarget TargetIn(MailFolderAlias folderAlias)
    {
        var folder = MailFolderResolution.FirstBindingOf(folderAlias, RemoteFolderPath.Create(folderAlias.Value));

        return new AuthoredMailboxTarget(
            EmailOccurrenceId.Create(Account, folder.Id, ImapUidValidity.Create(42), ImapUid.Create(7)),
            folder,
            StoredIn);
    }

    /// <summary>A session that reports the outcome its attempt was arranged with, signalling before it answers so a test can advance past the backoff only once the losing commit reported.</summary>
    private sealed class OutcomeSession(PersistenceCommitResult outcome, TaskCompletionSource? committed = null) : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken)
        {
            committed?.SetResult();

            return Task.FromResult(outcome);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
