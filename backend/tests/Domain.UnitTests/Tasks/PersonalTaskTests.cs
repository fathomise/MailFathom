// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Tasks;
using Xunit;

namespace MailFathom.Domain.UnitTests.Tasks;

/// <summary>Covers what a task may be built from, and the two things only a stored row may say about one.</summary>
public sealed class PersonalTaskTests
{
    private static readonly UserId User = UserId.Create(Guid.NewGuid());

    private static readonly PersonalTaskId Identifier = PersonalTaskId.Create(Guid.NewGuid());

    private static readonly DateTimeOffset Began = new(2026, 11, 19, 8, 0, 0, TimeSpan.Zero);

    /// <summary>A task a person entered stands outstanding, asserted, and citing nothing.</summary>
    [Fact]
    public void Compose_ATaskAPersonEntered_StandsOutstandingAndAsserted()
    {
        // Arrange
        var dueOn = new DateOnly(2026, 9, 27);

        // Act
        var task = PersonalTask.Compose(
            Identifier,
            User,
            "Send the counter-proposal",
            dueOn,
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Asserted,
            sourceMessage: null,
            TaskCommitment.Owed,
            DateTimeOffset.UnixEpoch);

        // Assert
        Assert.Equal(Identifier, task.Id);
        Assert.Equal(User, task.User);
        Assert.Equal("Send the counter-proposal", task.Title);
        Assert.Equal(dueOn, task.DueOn);
        Assert.Equal(PersonalTaskOrigin.Asserted, task.Origin);
        Assert.Null(task.SourceMessage);
        Assert.False(task.IsCompleted);
    }

    /// <summary>A task read out of mail cites the message it was read from, whichever origin it carries.</summary>
    [Fact]
    public void Compose_ATaskReadOutOfMail_CitesTheMessageItWasReadFrom()
    {
        // Arrange
        var message = StoredEmailId.Create(Guid.NewGuid());

        // Act
        var task = PersonalTask.Compose(
            Identifier,
            User,
            "Confirm the delivery date",
            dueOn: null,
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Proposed,
            message,
            TaskCommitment.Owed,
            DateTimeOffset.UnixEpoch);

        // Assert
        Assert.Equal(PersonalTaskOrigin.Proposed, task.Origin);
        Assert.Equal(message, task.SourceMessage);
    }

    /// <summary>Nobody has to say when, which is what leaves a task off every dated grouping until they do.</summary>
    [Fact]
    public void Compose_ATaskNobodyHasDated_CarriesNoDueDay()
    {
        // Act
        var task = PersonalTask.Compose(
            Identifier,
            User,
            "Close the budget",
            dueOn: null,
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Asserted,
            sourceMessage: null,
            TaskCommitment.Owed,
            DateTimeOffset.UnixEpoch);

        // Assert
        Assert.Null(task.DueOn);
    }

    /// <summary>Surrounding whitespace is not part of the line a list is drawn with.</summary>
    [Fact]
    public void Compose_ATitleWrittenWithSurroundingWhitespace_KeepsOnlyTheLineItself()
    {
        // Act
        var task = PersonalTask.Compose(
            Identifier,
            User,
            "  Sign the NDA \n",
            dueOn: null,
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Asserted,
            sourceMessage: null,
            TaskCommitment.Owed,
            DateTimeOffset.UnixEpoch);

        // Assert
        Assert.Equal("Sign the NDA", task.Title);
    }

    /// <summary>A task is a line on a list, so a title past the stored bound is refused rather than truncated.</summary>
    [Fact]
    public void Compose_ATitlePastTheStoredBound_IsRefused()
    {
        // Arrange
        var title = new string('x', PersonalTask.MaximumTitleLength + 1);

        // Act
        var composing = () => PersonalTask.Compose(
            Identifier,
            User,
            title,
            dueOn: null,
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Asserted,
            sourceMessage: null,
            TaskCommitment.Owed,
            DateTimeOffset.UnixEpoch);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(composing);
    }

    /// <summary>A blank title names nothing a person could act on.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Compose_ABlankTitle_IsRefused(string title)
    {
        // Act
        var composing = () => PersonalTask.Compose(
            Identifier,
            User,
            title,
            dueOn: null,
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Asserted,
            sourceMessage: null,
            TaskCommitment.Owed,
            DateTimeOffset.UnixEpoch);

        // Assert
        Assert.Throws<ArgumentException>(composing);
    }

    /// <summary>A task held under the unspecified user would belong to nobody, so it is never built.</summary>
    [Fact]
    public void Compose_TheUnspecifiedUser_IsRefused()
    {
        // Act
        var composing = () => PersonalTask.Compose(
            Identifier,
            default,
            "Sign the NDA",
            dueOn: null,
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Asserted,
            sourceMessage: null,
            TaskCommitment.Owed,
            DateTimeOffset.UnixEpoch);

        // Assert
        Assert.Throws<ArgumentException>(composing);
    }

    /// <summary>An identifier that addresses nothing is reachable as the struct default and is refused here.</summary>
    [Fact]
    public void Compose_AnIdentifierAddressingNothing_IsRefused()
    {
        // Act
        var composing = () => PersonalTask.Compose(
            default,
            User,
            "Sign the NDA",
            dueOn: null,
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Asserted,
            sourceMessage: null,
            TaskCommitment.Owed,
            DateTimeOffset.UnixEpoch);

        // Assert
        Assert.Throws<ArgumentException>(composing);
    }

    /// <summary>An origin this build does not declare says nothing about whether the person committed to the task.</summary>
    [Fact]
    public void Compose_AnUndeclaredOrigin_IsRefused()
    {
        // Act
        var composing = () => PersonalTask.Compose(
            Identifier,
            User,
            "Sign the NDA",
            dueOn: null,
            TaskAnnouncement.Silent,
            (PersonalTaskOrigin)42,
            sourceMessage: null,
            TaskCommitment.Owed,
            DateTimeOffset.UnixEpoch);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(composing);
    }

    /// <summary>Completion is the store's to say, which is why restoring takes it and composing does not.</summary>
    [Fact]
    public void Restore_ATaskThePersonHasDone_ReadsBackAsCompleted()
    {
        // Act
        var task = PersonalTask.Restore(
            Identifier,
            User,
            "Sign the NDA",
            new DateOnly(2026, 9, 12),
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Asserted,
            sourceMessage: null,
            isCompleted: true,
            TaskCommitment.Owed,
            answer: null,
            waitingSince: null);

        // Assert
        Assert.True(task.IsCompleted);
    }

    /// <summary>A row read back is input from outside this process, so it is held to what composing is held to.</summary>
    [Fact]
    public void Restore_ATitlePastTheStoredBound_IsRefused()
    {
        // Arrange
        var title = new string('x', PersonalTask.MaximumTitleLength + 1);

        // Act
        var restoring = () => PersonalTask.Restore(
            Identifier,
            User,
            title,
            dueOn: null,
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Asserted,
            sourceMessage: null,
            isCompleted: false,
            TaskCommitment.Owed,
            answer: null,
            waitingSince: null);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(restoring);
    }

    /// <summary>An edit is about the line and the day, and the rest of the record is not the editor's to state.</summary>
    [Fact]
    public void Revise_ATaskAPersonEdited_WritesTheLineAndTheDayAndKeepsEverythingElse()
    {
        // Arrange
        var cited = StoredEmailId.Create(Guid.NewGuid());
        var proposed = PersonalTask.Restore(
            Identifier,
            User,
            "Reply to the tender",
            new DateOnly(2026, 9, 27),
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Proposed,
            cited,
            isCompleted: true,
            TaskCommitment.Owed,
            answer: null,
            waitingSince: null);

        // Act
        var revised = proposed.Revise("  Reply to the tender by Friday  ", dueOn: null, TaskAnnouncement.Silent, TaskCommitment.Owed, DateTimeOffset.UnixEpoch);

        // Assert
        Assert.Equal("Reply to the tender by Friday", revised.Title);
        Assert.Null(revised.DueOn);
        Assert.Equal(Identifier, revised.Id);
        Assert.Equal(User, revised.User);
        Assert.Equal(PersonalTaskOrigin.Proposed, revised.Origin);
        Assert.Equal(cited, revised.SourceMessage);
        Assert.True(revised.IsCompleted);
    }

    /// <summary>The title is held to what composing holds it to, because an edit is where a person's own text arrives.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Revise_ATitleStatingNothing_IsRefused(string title)
    {
        // Arrange
        var task = PersonalTask.Compose(
            Identifier,
            User,
            "Sign the NDA",
            dueOn: null,
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Asserted,
            sourceMessage: null,
            TaskCommitment.Owed,
            DateTimeOffset.UnixEpoch);

        // Act
        var revising = () => task.Revise(title, dueOn: null, TaskAnnouncement.Silent, TaskCommitment.Owed, DateTimeOffset.UnixEpoch);

        // Assert
        Assert.Throws<ArgumentException>(revising);
    }

    /// <summary>The stored bound holds however the text arrives, so an edit cannot write a title a row could not be restored from.</summary>
    [Fact]
    public void Revise_ATitlePastTheStoredBound_IsRefused()
    {
        // Arrange
        var task = PersonalTask.Compose(
            Identifier,
            User,
            "Sign the NDA",
            dueOn: null,
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Asserted,
            sourceMessage: null,
            TaskCommitment.Owed,
            DateTimeOffset.UnixEpoch);

        // Act
        var revising = () => task.Revise(new string('x', PersonalTask.MaximumTitleLength + 1), dueOn: null, TaskAnnouncement.Silent, TaskCommitment.Owed, DateTimeOffset.UnixEpoch);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(revising);
    }

    /// <summary>A task a person waits for keeps whom it waits on and the conversation it is about.</summary>
    [Fact]
    public void Compose_AnAwaitedTaskWithAThreadAnchor_KeepsWhoAndWhere()
    {
        // Arrange
        Assert.True(EmailAddress.TryCreate("Ada", "ada@example.com", out var address));
        var anchor = TaskAnchor.Thread(EmailThreadId.Create(Guid.NewGuid()), "Tender reply");
        var counterpart = new TaskCounterpart(address, "  Ada Lovelace ");
        var plan = TaskPlan.Create(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero), 30);

        // Act
        var task = PersonalTask.Compose(
            Identifier,
            User,
            "Hear back about the tender",
            dueOn: null,
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Asserted,
            sourceMessage: null,
            new TaskCommitment(PersonalTaskDirection.Awaited, 15, plan, anchor, counterpart),
            DateTimeOffset.UnixEpoch);

        // Assert
        Assert.Equal(PersonalTaskDirection.Awaited, task.Direction);
        Assert.Equal(15, task.EstimatedMinutes);
        Assert.Equal(plan, task.Plan);
        Assert.Equal(anchor, task.Anchor);
        Assert.Equal(counterpart, task.Counterpart);
        Assert.Equal("Ada Lovelace", task.Counterpart?.DisplayName);
        Assert.Null(task.Answer);
    }

    /// <summary>An estimate outside what a plan may set aside is refused, composed or restored.</summary>
    [Theory]
    [InlineData(4)]
    [InlineData(721)]
    public void Compose_AnEstimateOutsideFiveMinutesToTwelveHours_IsRefused(int minutes)
    {
        // Act
        var composing = () => PersonalTask.Compose(
            Identifier,
            User,
            "Sign the NDA",
            dueOn: null,
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Asserted,
            sourceMessage: null,
            TaskCommitment.Owed with { EstimatedMinutes = minutes },
            DateTimeOffset.UnixEpoch);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(composing);
    }

    /// <summary>An edit restates the commitment but leaves the reply that answered the task where it was.</summary>
    [Fact]
    public void Revise_AnAnsweredWaitingTask_KeepsTheAnswer()
    {
        // Arrange
        var answer = new TaskAnswer(StoredEmailId.Create(Guid.NewGuid()), new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.Zero));
        var answered = Waiting().Answered(answer);

        // Act
        var revised = answered.Revise("Hear back by Friday", dueOn: null, TaskAnnouncement.Silent, answered.Commitment with { EstimatedMinutes = 10 }, DateTimeOffset.UnixEpoch);

        // Assert
        Assert.Equal(answer, revised.Answer);
        Assert.True(revised.IsCompleted);
        Assert.Equal(10, revised.EstimatedMinutes);
        Assert.Equal(PersonalTaskDirection.Awaited, revised.Direction);
    }

    /// <summary>What a person owes is done by them, and no reply settles it.</summary>
    [Fact]
    public void Answered_AnOwedTask_IsRefused()
    {
        // Arrange
        var owed = PersonalTask.Compose(
            Identifier,
            User,
            "Sign the NDA",
            dueOn: null,
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Asserted,
            sourceMessage: null,
            TaskCommitment.Owed,
            DateTimeOffset.UnixEpoch);

        // Act
        var answering = () => owed.Answered(AReply());

        // Assert
        Assert.Throws<InvalidOperationException>(answering);
    }

    /// <summary>A reply completes the wait and is cited rather than copied.</summary>
    [Fact]
    public void Answered_AnAwaitedTask_CompletesItAndCitesTheReply()
    {
        // Arrange
        var reply = AReply();

        // Act
        var answered = Waiting().Answered(reply);

        // Assert
        Assert.True(answered.IsCompleted);
        Assert.Equal(reply, answered.Answer);
        Assert.Equal(PersonalTaskDirection.Awaited, answered.Direction);
    }

    /// <summary>A wait that is already over is not answered a second time.</summary>
    [Fact]
    public void Answered_ACompletedAwaitedTask_IsRefused()
    {
        // Arrange
        var answered = Waiting().Answered(AReply());

        // Act
        var answering = () => answered.Answered(AReply());

        // Assert
        Assert.Throws<InvalidOperationException>(answering);
    }

    /// <summary>A reply is what closes a wait, so a row citing one on a task nobody waited for is refused rather than read.</summary>
    [Fact]
    public void Restore_AnAnswerOnAnOwedTask_IsRefused()
    {
        // Act
        var restoring = () => Restored(TaskCommitment.Owed, isCompleted: true, AReply());

        // Assert
        Assert.Throws<ArgumentException>(restoring);
    }

    /// <summary>An answer completes the wait it closes, so a row citing one on a task still open contradicts itself.</summary>
    [Fact]
    public void Restore_AnAnswerOnAnOpenAwaitedTask_IsRefused()
    {
        // Act
        var restoring = () => Restored(
            TaskCommitment.Owed with { Direction = PersonalTaskDirection.Awaited },
            isCompleted: false,
            AReply());

        // Assert
        Assert.Throws<ArgumentException>(restoring);
    }

    /// <summary>A wait the reply closed reads back with the reply it cites.</summary>
    [Fact]
    public void Restore_AnAnsweredAwaitedTask_ReadsBackWithItsAnswer()
    {
        // Arrange
        var reply = AReply();

        // Act
        var task = Restored(TaskCommitment.Owed with { Direction = PersonalTaskDirection.Awaited }, isCompleted: true, reply);

        // Assert
        Assert.Equal(reply, task.Answer);
    }

    /// <summary>The answer belongs to the wait it closed, so a task it answered is never turned into something owed.</summary>
    [Fact]
    public void Revise_TurningAnAnsweredWaitingTaskIntoAnOwedOne_IsRefused()
    {
        // Arrange
        var answered = Waiting().Answered(AReply());

        // Act
        var revising = () => answered.Revise(
            answered.Title,
            dueOn: null,
            TaskAnnouncement.Silent,
            answered.Commitment with { Direction = PersonalTaskDirection.Owed },
            DateTimeOffset.UnixEpoch);

        // Assert
        Assert.Throws<InvalidOperationException>(revising);
    }

    private static PersonalTask Restored(TaskCommitment commitment, bool isCompleted, TaskAnswer answer) => PersonalTask.Restore(
        Identifier,
        User,
        "Hear back about the tender",
        dueOn: null,
        TaskAnnouncement.Silent,
        PersonalTaskOrigin.Asserted,
        sourceMessage: null,
        isCompleted,
        commitment,
        answer,
        waitingSince: commitment.Direction is PersonalTaskDirection.Awaited ? DateTimeOffset.UnixEpoch : null);

    /// <summary>A wait begins when it is written down, and a task the person owes begins no wait.</summary>
    [Fact]
    public void Compose_AWaitAndAnOwedTask_StateWhenTheWaitBeganOnlyForTheWait()
    {
        // Act
        var waiting = Waiting();
        var owed = Waiting().Revise("Send the tender", dueOn: null, TaskAnnouncement.Silent, TaskCommitment.Owed, Began.AddDays(1));

        // Assert
        Assert.Equal(Began, waiting.WaitingSince);
        Assert.Null(owed.WaitingSince);
    }

    /// <summary>Editing a wait keeps when it began, and an owed task turned into a wait begins waiting at the edit.</summary>
    [Fact]
    public void Revise_TheDirection_MovesWhenTheWaitBeganOnlyWhenTheTaskBecomesAWait()
    {
        // Arrange
        var edited = Began.AddDays(1);
        var awaited = TaskCommitment.Owed with { Direction = PersonalTaskDirection.Awaited };
        var owed = Waiting().Revise("Send the tender", dueOn: null, TaskAnnouncement.Silent, TaskCommitment.Owed, edited);

        // Act
        var stillWaiting = Waiting().Revise("Hear back by Friday", dueOn: null, TaskAnnouncement.Silent, awaited, edited);
        var nowWaiting = owed.Revise("Hear back by Friday", dueOn: null, TaskAnnouncement.Silent, awaited, edited.AddDays(1));

        // Assert
        Assert.Equal(Began, stillWaiting.WaitingSince);
        Assert.Equal(edited.AddDays(1), nowWaiting.WaitingSince);
    }

    /// <summary>A row read back states when a wait began exactly when it is a wait.</summary>
    [Theory]
    [InlineData(PersonalTaskDirection.Awaited, false)]
    [InlineData(PersonalTaskDirection.Owed, true)]
    public void Restore_WhenTheWaitBeganOnTheWrongDirection_IsRefused(PersonalTaskDirection direction, bool statesWhenItBegan)
    {
        // Act
        var restoring = () => PersonalTask.Restore(
            Identifier,
            User,
            "Hear back about the tender",
            dueOn: null,
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Asserted,
            sourceMessage: null,
            isCompleted: false,
            TaskCommitment.Owed with { Direction = direction },
            answer: null,
            statesWhenItBegan ? Began : null);

        // Assert
        Assert.Equal("waitingSince", Assert.Throws<ArgumentException>(restoring).ParamName);
    }

    /// <summary>Only a reply that arrived after the wait began answers it, and only while it is open.</summary>
    [Theory]
    [InlineData(-1, false, false)]
    [InlineData(0, false, false)]
    [InlineData(1, false, true)]
    [InlineData(1, true, false)]
    public void IsAnswerableBy_AReplyArrivingAroundWhenTheWaitBegan_AnswersOnlyAfterIt(int minutesAfter, bool completed, bool answers)
    {
        // Arrange
        var task = completed ? Waiting().Answered(AReply()) : Waiting();

        // Act
        var answerable = task.IsAnswerableBy(new TaskAnswer(StoredEmailId.Create(Guid.NewGuid()), Began.AddMinutes(minutesAfter)));

        // Assert
        Assert.Equal(answers, answerable);
    }

    private static TaskAnswer AReply() => new(StoredEmailId.Create(Guid.NewGuid()), new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.Zero));

    private static PersonalTask Waiting() => PersonalTask.Compose(
        Identifier,
        User,
        "Hear back about the tender",
        dueOn: null,
        TaskAnnouncement.Silent,
        PersonalTaskOrigin.Asserted,
        sourceMessage: null,
        TaskCommitment.Owed with { Direction = PersonalTaskDirection.Awaited },
        Began);
}
