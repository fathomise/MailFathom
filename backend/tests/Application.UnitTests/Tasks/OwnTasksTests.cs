// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Signals;
using MailFathom.Application.Tasks;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Tasks;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Tasks;

/// <summary>
/// Covers the use case a person reads and writes their own task list through. What it has to hold is that the person
/// acted on is the one the credential authenticated rather than one a caller could name, that the two origins are read
/// apart and a cursor does not cross between them, that a page is bounded and clamped rather than refused, and that a
/// task somebody else holds answers exactly as one that does not exist.
/// </summary>
public sealed class OwnTasksTests
{
    private static readonly UserId User = SyntheticUser.Deployment;

    private static readonly UserId SomebodyElse = UserId.Create(
        new Guid("6d0b6a1c-6f5e-4a7e-9a1a-8d2a3f4b5c60"));

    private static readonly DateTimeOffset Stamped = new(2026, 9, 20, 9, 0, 0, TimeSpan.Zero);

    private static readonly EmailThreadId Thread = EmailThreadId.Create(new Guid("0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a92"));

    [Fact]
    public async Task ReadPageAsync_APersonWithTasks_AnswersTheirOwnSoonestDueFirstWithTheUndatedLast()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        await KeepAsync(store, User, "undated", dueOn: null);
        await KeepAsync(store, User, "later", new DateOnly(2026, 10, 1));
        await KeepAsync(store, User, "sooner", new DateOnly(2026, 9, 21));
        await KeepAsync(store, SomebodyElse, "theirs", new DateOnly(2026, 9, 20));

        // Act
        var page = await SignedIn(store).ReadPageAsync(
            new PersonalTaskQuery(PersonalTaskOrigin.Asserted),
            null,
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["sooner", "later", "undated"], page!.Tasks.Select(task => task.Title));
    }

    /// <summary>What mail suggested is an offer rather than a commitment, so it is read apart from the list of what is owed.</summary>
    [Fact]
    public async Task ReadPageAsync_TheProposedOrigin_AnswersWhatMailSuggestedAndNothingCommittedTo()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        await KeepAsync(store, User, "committed", new DateOnly(2026, 9, 21));
        await KeepAsync(store, User, "suggested", new DateOnly(2026, 9, 22), PersonalTaskOrigin.Proposed);

        // Act
        var page = await SignedIn(store).ReadPageAsync(
            new PersonalTaskQuery(PersonalTaskOrigin.Proposed),
            null,
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("suggested", Assert.Single(page!.Tasks).Title);
    }

    /// <summary>A panel asks for as much as it can draw, so the useful answer to a number no screen wants is the most this deployment serves.</summary>
    [Theory]
    [InlineData(null, OwnTasks.DefaultPageSize)]
    [InlineData(0, OwnTasks.DefaultPageSize)]
    [InlineData(-5, OwnTasks.DefaultPageSize)]
    [InlineData(1000, OwnTasks.MaximumPageSize)]
    [InlineData(7, 7)]
    public async Task ReadPageAsync_APageSizeAskedFor_IsClampedToWhatThisDeploymentServes(int? asked, int served)
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();

        // Act
        await SignedIn(store).ReadPageAsync(
            new PersonalTaskQuery(PersonalTaskOrigin.Asserted),
            asked,
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(served, store.LastLimit);
    }

    /// <summary>The boundary continues the walk rather than shifting a window, so a task dated while somebody pages neither repeats nor skips a row.</summary>
    [Fact]
    public async Task ReadPageAsync_TheCursorAPreviousPageReturned_ContinuesBeyondIt()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        await KeepAsync(store, User, "sooner", new DateOnly(2026, 9, 21));
        await KeepAsync(store, User, "later", new DateOnly(2026, 10, 1));

        var tasks = SignedIn(store);
        var first = await tasks.ReadPageAsync(
            new PersonalTaskQuery(PersonalTaskOrigin.Asserted),
            1,
            null,
            TestContext.Current.CancellationToken);

        // Act
        var second = await tasks.ReadPageAsync(
            new PersonalTaskQuery(PersonalTaskOrigin.Asserted),
            1,
            first!.NextCursor,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("sooner", Assert.Single(first.Tasks).Title);
        Assert.Equal("later", Assert.Single(second!.Tasks).Title);
    }

    /// <summary>The undated block is a position like any other, so a walk that ended inside it continues through the rest of it.</summary>
    [Fact]
    public async Task ReadPageAsync_ACursorInsideTheUndatedBlock_ContinuesThroughThatBlock()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        await KeepAsync(store, User, "first-undated", dueOn: null);
        await KeepAsync(store, User, "second-undated", dueOn: null);

        var tasks = SignedIn(store);
        var first = await tasks.ReadPageAsync(
            new PersonalTaskQuery(PersonalTaskOrigin.Asserted),
            1,
            null,
            TestContext.Current.CancellationToken);

        // Act
        var second = await tasks.ReadPageAsync(
            new PersonalTaskQuery(PersonalTaskOrigin.Asserted),
            1,
            first!.NextCursor,
            TestContext.Current.CancellationToken);

        // Assert
        var walked = Assert.Single(second!.Tasks);
        Assert.Null(walked.DueOn);
        Assert.NotEqual(Assert.Single(first.Tasks).Id, walked.Id);
    }

    /// <summary>A short page is the end of the list, so a caller stops on the absent cursor rather than on a length comparison.</summary>
    [Fact]
    public async Task ReadPageAsync_APageTheListCouldNotFill_CarriesNoCursor()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        await KeepAsync(store, User, "only", new DateOnly(2026, 9, 21));

        // Act
        var page = await SignedIn(store).ReadPageAsync(
            new PersonalTaskQuery(PersonalTaskOrigin.Asserted),
            10,
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(page!.NextCursor);
    }

    /// <summary>The origin is part of what the cursor was issued for, so a walk of the proposals names no boundary in the commitments.</summary>
    [Fact]
    public async Task ReadPageAsync_ACursorIssuedForTheOtherOrigin_IsRefused()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        await KeepAsync(store, User, "suggested", new DateOnly(2026, 9, 21), PersonalTaskOrigin.Proposed);
        await KeepAsync(store, User, "also-suggested", new DateOnly(2026, 9, 22), PersonalTaskOrigin.Proposed);

        var tasks = SignedIn(store);
        var proposed = await tasks.ReadPageAsync(
            new PersonalTaskQuery(PersonalTaskOrigin.Proposed),
            1,
            null,
            TestContext.Current.CancellationToken);

        // Act
        var committed = await tasks.ReadPageAsync(
            new PersonalTaskQuery(PersonalTaskOrigin.Asserted),
            1,
            proposed!.NextCursor,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(committed);
    }

    /// <summary>The fingerprint carries the user, so a cursor issued to somebody else names no boundary in this caller's own walk.</summary>
    [Fact]
    public async Task ReadPageAsync_ACursorIssuedForAnotherUser_IsRefused()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        await KeepAsync(store, SomebodyElse, "theirs", new DateOnly(2026, 9, 21));
        await KeepAsync(store, SomebodyElse, "theirs-too", new DateOnly(2026, 9, 22));

        var elsewhere = await Signed(store, SomebodyElse).ReadPageAsync(
            new PersonalTaskQuery(PersonalTaskOrigin.Asserted),
            1,
            null,
            TestContext.Current.CancellationToken);

        // Act
        var page = await SignedIn(store).ReadPageAsync(
            new PersonalTaskQuery(PersonalTaskOrigin.Asserted),
            1,
            elsewhere!.NextCursor,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(page);
    }

    /// <summary>A boundary this deployment never issued names no page, and answering it with the first one would be a list silently jumping back to the top.</summary>
    [Fact]
    public async Task ReadPageAsync_ACursorThisDeploymentDidNotIssue_IsRefusedRatherThanIgnored()
    {
        // Act
        var page = await SignedIn(new InMemoryPersonalTaskStore()).ReadPageAsync(
            new PersonalTaskQuery(PersonalTaskOrigin.Asserted),
            10,
            "not-a-cursor-this-deployment-issued",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(page);
    }

    /// <summary>Completed tasks are the screen's decision rather than the list's, so they are served with the outstanding ones.</summary>
    [Fact]
    public async Task ReadPageAsync_ACompletedTask_IsServedWithTheOutstandingOnes()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        var done = await KeepAsync(store, User, "done", new DateOnly(2026, 9, 21));
        var tasks = SignedIn(store);
        await tasks.SetCompletionAsync(done.Id, true, TestContext.Current.CancellationToken);

        // Act
        var page = await tasks.ReadPageAsync(
            new PersonalTaskQuery(PersonalTaskOrigin.Asserted),
            10,
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(Assert.Single(page!.Tasks).IsCompleted);
    }

    /// <summary>What a person types into their own client is something they owe, so this is not the seam a proposal arrives through.</summary>
    [Fact]
    public async Task RecordAsync_ATaskAPersonTyped_WritesItAssertedForThemAndOutstanding()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        var cited = StoredEmailId.Create(new Guid("0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90"));

        // Act
        var written = await SignedIn(store).RecordAsync(
            "  Answer the tender  ",
            new DateOnly(2026, 9, 21),
            TaskAnnouncement.Silent,
            TaskCommitment.Owed,
            cited,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Answer the tender", written.Title);
        Assert.Equal(User, written.User);
        Assert.Equal(PersonalTaskOrigin.Asserted, written.Origin);
        Assert.Equal(cited, written.SourceMessage);
        Assert.False(written.IsCompleted);
        Assert.Equal(written, Assert.Single(store.Held));
    }

    [Fact]
    public async Task ReviseAsync_ATaskOfTheirOwn_WritesTheLineAndTheDayAndLeavesTheRest()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        var kept = await KeepAsync(store, User, "draft the reply", new DateOnly(2026, 9, 21), PersonalTaskOrigin.Proposed);

        // Act
        var revised = await SignedIn(store).ReviseAsync(
            kept.Id,
            "draft the reply to the tender",
            dueOn: null,
            TaskAnnouncement.Silent,
            TaskCommitment.Owed,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(PersonalTaskChangeOutcome.Applied, revised.Outcome);
        Assert.Equal("draft the reply to the tender", revised.Task!.Title);
        Assert.Null(revised.Task.DueOn);
        Assert.Equal(PersonalTaskOrigin.Proposed, revised.Task.Origin);
        Assert.Equal(revised.Task, Assert.Single(store.Held));
    }

    /// <summary>The answer is the same one an absent task gets, so nothing here reports whose tasks exist.</summary>
    [Fact]
    public async Task ReviseAsync_ATaskSomebodyElseHolds_AnswersAsOneThatDoesNotExist()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        var theirs = await KeepAsync(store, SomebodyElse, "theirs", new DateOnly(2026, 9, 21));

        // Act
        var revised = await SignedIn(store).ReviseAsync(
            theirs.Id,
            "mine now",
            dueOn: null,
            TaskAnnouncement.Silent,
            TaskCommitment.Owed,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(PersonalTaskRevisionResult.NotFound, revised);
        Assert.Equal("theirs", Assert.Single(store.Held).Title);
    }

    /// <summary>A wait is recorded with what it waits on and who it waits for, so a reply in that thread can close it.</summary>
    [Fact]
    public async Task RecordAsync_AWaitingTaskForAThread_AnswersItWithItsAnchorAndCounterpart()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        var commitment = new TaskCommitment(
            PersonalTaskDirection.Awaited,
            EstimatedMinutes: 15,
            Plan: null,
            TaskAnchor.Thread(Thread, "Tender questions"),
            Counterpart());

        // Act
        var written = await SignedIn(store).RecordAsync(
            "Hear back about the tender",
            dueOn: null,
            TaskAnnouncement.Silent,
            commitment,
            sourceMessage: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(commitment, written.Commitment);
        Assert.Equal(commitment, Assert.Single(store.Held).Commitment);
    }

    /// <summary>A revision writes the commitment it states, so a plan dragged onto the calendar is there when the list is read again.</summary>
    [Fact]
    public async Task ReviseAsync_APlan_IsKeptAcrossAReread()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        var kept = await KeepAsync(store, User, "draft the reply", new DateOnly(2026, 9, 21));
        var plan = TaskPlan.Create(new DateTimeOffset(2026, 9, 21, 14, 0, 0, TimeSpan.FromHours(2)), 45);
        var tasks = SignedIn(store);

        // Act
        await tasks.ReviseAsync(
            kept.Id,
            "draft the reply",
            new DateOnly(2026, 9, 21),
            TaskAnnouncement.Silent,
            TaskCommitment.Owed with { EstimatedMinutes = 45, Plan = plan },
            TestContext.Current.CancellationToken);

        // Assert
        var reread = await tasks.FindAsync(kept.Id, TestContext.Current.CancellationToken);

        Assert.Equal(plan, reread!.Plan);
        Assert.Equal(45, reread.EstimatedMinutes);
    }

    /// <summary>The reply closed a wait, so the person turning it into something they owe is refused rather than rewriting what the reply answered.</summary>
    [Fact]
    public async Task ReviseAsync_TheDirectionOfAWaitAReplyAnswered_IsRefusedAndWritesNothing()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        var waiting = await KeepAsync(store, User, "hear back", dueOn: null, commitment: Awaiting());
        await store.AnswerAsync(User, waiting.Id, Answer(), TestContext.Current.CancellationToken);

        // Act
        var revised = await SignedIn(store).ReviseAsync(
            waiting.Id,
            "hear back",
            dueOn: null,
            TaskAnnouncement.Silent,
            TaskCommitment.Owed,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(PersonalTaskChangeOutcome.AnsweredWait, revised.Outcome);
        Assert.Null(revised.Task);
        Assert.Equal(PersonalTaskDirection.Awaited, Assert.Single(store.Held).Direction);
    }

    /// <summary>A reply can land between reading the wait and writing the change, and the store refusing it then is the same refusal.</summary>
    [Fact]
    public async Task ReviseAsync_AReplyAnsweringTheWaitWhileItsDirectionChanges_IsRefusedWithoutAnnouncingAnything()
    {
        // Arrange
        var waiting = PersonalTask.Compose(
            PersonalTaskId.Create(Guid.CreateVersion7(Stamped)),
            User,
            "hear back",
            dueOn: null,
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Asserted,
            sourceMessage: null,
            Awaiting());
        var store = Substitute.For<IPersonalTaskStore>();
        store.FindAsync(User, waiting.Id, Arg.Any<CancellationToken>()).Returns(waiting);
        store.ReviseAsync(Arg.Any<PersonalTask>(), Arg.Any<CancellationToken>())
            .Returns(PersonalTaskChangeOutcome.AnsweredWait);
        var clock = new FakeTimeProvider(Stamped);
        var channel = new RecordingClientSignalChannel();
        await using var signals = new ClientSignals([channel], clock);

        // Act
        var revised = await Signed(store, User, signals).ReviseAsync(
            waiting.Id,
            "hear back",
            dueOn: null,
            TaskAnnouncement.Silent,
            TaskCommitment.Owed,
            TestContext.Current.CancellationToken);

        // Assert
        clock.Advance(ClientSignals.FoldingWindow);
        await signals.DrainAsync();

        Assert.Equal(PersonalTaskChangeOutcome.AnsweredWait, revised.Outcome);
        Assert.Empty(channel.Published);
    }

    /// <summary>The whole narrowing is what a cursor was issued for, so a walk of one thread's tasks names no boundary in another's.</summary>
    [Fact]
    public async Task ReadPageAsync_ACursorIssuedUnderAnotherNarrowing_IsRefused()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        await KeepAsync(store, User, "first", new DateOnly(2026, 9, 21), commitment: Awaiting());
        await KeepAsync(store, User, "second", new DateOnly(2026, 9, 22), commitment: Awaiting());

        var tasks = SignedIn(store);
        var narrowed = new PersonalTaskQuery(PersonalTaskOrigin.Asserted, Thread);
        var first = await tasks.ReadPageAsync(narrowed, 1, null, TestContext.Current.CancellationToken);

        // Act
        var other = await tasks.ReadPageAsync(
            narrowed with { Counterpart = Counterpart().Address },
            1,
            first!.NextCursor,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(first.NextCursor);
        Assert.Null(other);
    }

    /// <summary>Accepting moves the row that already exists, so a commitment keeps the identity it was proposed under.</summary>
    [Fact]
    public async Task AcceptAsync_AProposalOfTheirOwn_MovesItsOriginAndKeepsItsIdentity()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        var proposed = await KeepAsync(store, User, "call them back", dueOn: null, PersonalTaskOrigin.Proposed);

        // Act
        var accepted = await SignedIn(store).AcceptAsync(proposed.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(proposed.Id, accepted!.Id);
        Assert.Equal(PersonalTaskOrigin.Asserted, accepted.Origin);
        Assert.Equal(proposed.Id, Assert.Single(store.Held).Id);
    }

    /// <summary>A state a task already stands in is the state the caller asked for, so accepting twice writes once and reports done.</summary>
    [Fact]
    public async Task AcceptAsync_ATaskAlreadyAsserted_IsAnsweredAsDone()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        var committed = await KeepAsync(store, User, "already mine", dueOn: null);

        // Act
        var accepted = await SignedIn(store).AcceptAsync(committed.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(PersonalTaskOrigin.Asserted, accepted!.Origin);
    }

    [Fact]
    public async Task SetCompletionAsync_ATaskOfTheirOwn_CompletesItAndPutsItBack()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        var kept = await KeepAsync(store, User, "file the return", new DateOnly(2026, 9, 21));
        var tasks = SignedIn(store);

        // Act
        var completed = await tasks.SetCompletionAsync(kept.Id, true, TestContext.Current.CancellationToken);
        var outstanding = await tasks.SetCompletionAsync(kept.Id, false, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(completed!.IsCompleted);
        Assert.False(outstanding!.IsCompleted);
    }

    /// <summary>The identifier is not a capability: the write carries the caller inside it, so somebody else's task is neither moved nor reported.</summary>
    [Fact]
    public async Task SetCompletionAsync_ATaskSomebodyElseHolds_AnswersAsOneThatDoesNotExist()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        var theirs = await KeepAsync(store, SomebodyElse, "theirs", dueOn: null);

        // Act
        var changed = await SignedIn(store).SetCompletionAsync(
            theirs.Id,
            true,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(changed);
        Assert.False(Assert.Single(store.Held).IsCompleted);
    }

    /// <summary>Dismissing a proposal is this same erasure, which is why there is no second act for it.</summary>
    [Fact]
    public async Task EraseAsync_AProposalOfTheirOwn_TakesItOutOfTheList()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        var proposed = await KeepAsync(store, User, "suggested", dueOn: null, PersonalTaskOrigin.Proposed);

        // Act
        var erased = await SignedIn(store).EraseAsync(proposed.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(erased);
        Assert.Empty(store.Held);
    }

    /// <summary>The act is what the caller wanted either way, so a task that has already gone is answered rather than refused.</summary>
    [Fact]
    public async Task EraseAsync_ATaskThatIsAlreadyGone_ErasesNothingAndDoesNotFail()
    {
        // Act
        var erased = await SignedIn(new InMemoryPersonalTaskStore()).EraseAsync(
            PersonalTaskId.Create(Guid.CreateVersion7(Stamped)),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(erased);
    }

    /// <summary>The list is a record of its own rather than anything in a mailbox, so the grant that reads mail reaches none of it.</summary>
    [Fact]
    public async Task ReadPageAsync_ACallerGrantedOnlyReadingMail_IsRefused()
    {
        // Arrange
        var tasks = new OwnTasks(
            AccessAuthorizations.ForUserGranted(User, MailFathomPermission.MailRead),
            new InMemoryPersonalTaskStore(),
            new FakeTimeProvider(Stamped),
            ClientSignalPublishers.ReachingNobody);

        // Act and assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => tasks.ReadPageAsync(
                new PersonalTaskQuery(PersonalTaskOrigin.Asserted),
                null,
                null,
                TestContext.Current.CancellationToken));
    }

    /// <summary>Reading the list is not changing it, so a caller granted only the reading half records nothing.</summary>
    [Fact]
    public async Task RecordAsync_ACallerGrantedOnlyReadingTheList_IsRefused()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        var tasks = new OwnTasks(
            AccessAuthorizations.ForUserGranted(User, MailFathomPermission.TasksRead),
            store,
            new FakeTimeProvider(Stamped),
            ClientSignalPublishers.ReachingNobody);

        // Act and assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => tasks.RecordAsync(
                "keep a list",
                dueOn: null,
                TaskAnnouncement.Silent,
                TaskCommitment.Owed,
                sourceMessage: null,
                TestContext.Current.CancellationToken));

        Assert.Empty(store.Held);
    }

    /// <summary>A grant that reads no mailbox still keeps this list, because nothing about a task is mail.</summary>
    [Fact]
    public async Task RecordAsync_ACallerGrantedOnlyChangingTheList_WritesTheTask()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        var tasks = new OwnTasks(
            AccessAuthorizations.ForUserGranted(User, MailFathomPermission.TasksWrite),
            store,
            new FakeTimeProvider(Stamped),
            ClientSignalPublishers.ReachingNobody);

        // Act
        await tasks.RecordAsync(
            "keep a list",
            dueOn: null,
            TaskAnnouncement.Silent,
            TaskCommitment.Owed,
            sourceMessage: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("keep a list", Assert.Single(store.Held).Title);
    }

    /// <summary>Each write that changed the list tells the person's other clients, and names nothing about the task.</summary>
    [Fact]
    public async Task EveryWriteThatChangedTheList_AnnouncesItToThePerson()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        var proposed = await KeepAsync(store, User, "suggested", dueOn: null, PersonalTaskOrigin.Proposed);
        var clock = new FakeTimeProvider(Stamped);
        var channel = new RecordingClientSignalChannel();
        await using var signals = new ClientSignals([channel], clock);
        var tasks = Signed(store, User, signals);

        // Act
        foreach (var write in new Func<Task>[]
        {
            () => tasks.RecordAsync("call back", null, TaskAnnouncement.Silent, TaskCommitment.Owed, null, TestContext.Current.CancellationToken),
            () => tasks.ReviseAsync(proposed.Id, "call back soon", null, TaskAnnouncement.Silent, TaskCommitment.Owed, TestContext.Current.CancellationToken),
            () => tasks.AcceptAsync(proposed.Id, TestContext.Current.CancellationToken),
            () => tasks.SetCompletionAsync(proposed.Id, isCompleted: true, TestContext.Current.CancellationToken),
            () => tasks.EraseAsync(proposed.Id, TestContext.Current.CancellationToken),
        })
        {
            await write();
            clock.Advance(ClientSignals.FoldingWindow);
            await signals.DrainAsync();
        }

        // Assert
        Assert.Equal(5, channel.Published.Count);
        Assert.All(channel.Published, signal => Assert.Equal(ClientSignal.TasksChanged(User).Scope, signal.Scope));
    }

    /// <summary>A write naming a task the person does not hold changed nothing, so it tells nobody anything.</summary>
    [Fact]
    public async Task WritesNamingATaskSomebodyElseHolds_AnnounceNothing()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        var theirs = await KeepAsync(store, SomebodyElse, "theirs", dueOn: null, PersonalTaskOrigin.Proposed);
        var clock = new FakeTimeProvider(Stamped);
        var channel = new RecordingClientSignalChannel();
        await using var signals = new ClientSignals([channel], clock);
        var tasks = Signed(store, User, signals);

        // Act
        await tasks.ReviseAsync(theirs.Id, "mine now", null, TaskAnnouncement.Silent, TaskCommitment.Owed, TestContext.Current.CancellationToken);
        await tasks.AcceptAsync(theirs.Id, TestContext.Current.CancellationToken);
        await tasks.EraseAsync(theirs.Id, TestContext.Current.CancellationToken);
        clock.Advance(ClientSignals.FoldingWindow);
        await signals.DrainAsync();

        // Assert
        Assert.Empty(channel.Published);
    }

    private static OwnTasks SignedIn(IPersonalTaskStore store) => Signed(store, User);

    private static OwnTasks Signed(IPersonalTaskStore store, UserId user, ClientSignals? signals = null) => new(
        AccessAuthorizations.ForUserGranted(user, MailFathomPermission.TasksRead, MailFathomPermission.TasksWrite),
        store,
        new FakeTimeProvider(Stamped),
        signals ?? ClientSignalPublishers.ReachingNobody);

    private static TaskCounterpart Counterpart() =>
        new(EmailAddress.TryCreate(null, "buyer@example.test", out var address) ? address : default, "The buyer");

    private static TaskCommitment Awaiting() => new(
        PersonalTaskDirection.Awaited,
        EstimatedMinutes: null,
        Plan: null,
        TaskAnchor.Thread(Thread, "Tender questions"),
        Counterpart());

    private static TaskAnswer Answer() => new(
        StoredEmailId.Create(new Guid("0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a91")),
        Stamped + TimeSpan.FromHours(1));

    private static async Task<PersonalTask> KeepAsync(
        InMemoryPersonalTaskStore store,
        UserId user,
        string title,
        DateOnly? dueOn,
        PersonalTaskOrigin origin = PersonalTaskOrigin.Asserted,
        TaskCommitment? commitment = null)
    {
        var task = PersonalTask.Compose(
            PersonalTaskId.Create(Guid.CreateVersion7(Stamped + TimeSpan.FromMinutes(store.Held.Count))),
            user,
            title,
            dueOn,
            TaskAnnouncement.Silent,
            origin,
            sourceMessage: null,
            commitment ?? TaskCommitment.Owed);

        await store.AddAsync(task, TestContext.Current.CancellationToken);

        return task;
    }
}
