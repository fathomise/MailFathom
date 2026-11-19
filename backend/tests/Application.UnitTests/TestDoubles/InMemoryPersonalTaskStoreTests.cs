// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Tasks;
using MailFathom.Domain.Calendar;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Tasks;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>
/// Pins the double to the narrowings the persisted store reads by, which <c>OrchestratedPersonalTaskStoreTests</c>
/// proves against PostgreSQL, so a use case tested over the double reads the list the deployment would.
/// </summary>
public sealed class InMemoryPersonalTaskStoreTests
{
    private const int Page = 50;

    private static readonly TimeSpan Warsaw = TimeSpan.FromHours(1);

    private static readonly DateTimeOffset Opens = new(2026, 11, 2, 0, 0, 0, Warsaw);

    [Fact]
    public async Task ReadAsync_ByThread_ReadsOnlyThatThreadsTasks()
    {
        // Arrange
        var thread = EmailThreadId.Create(Guid.NewGuid());
        var anchored = Task(TaskCommitment.Owed with { Anchor = TaskAnchor.Thread(thread, "Tender") });
        var elsewhere = Task(TaskCommitment.Owed with { Anchor = TaskAnchor.Thread(EmailThreadId.Create(Guid.NewGuid()), "Audit") });

        // Act
        var read = await ReadAsync([anchored, elsewhere], new PersonalTaskQuery(null, Thread: thread));

        // Assert
        Assert.Equal([anchored.Id], read.Select(task => task.Id));
    }

    [Fact]
    public async Task ReadAsync_ByEvent_ReadsOnlyThatEventsTasks()
    {
        // Arrange
        var calendarEvent = CalendarEventId.Create(Guid.NewGuid());
        var anchored = Task(TaskCommitment.Owed with { Anchor = TaskAnchor.Event(calendarEvent, "Kick-off") });
        var unanchored = Task(TaskCommitment.Owed);

        // Act
        var read = await ReadAsync([anchored, unanchored], new PersonalTaskQuery(null, Event: calendarEvent));

        // Assert
        Assert.Equal([anchored.Id], read.Select(task => task.Id));
    }

    [Fact]
    public async Task ReadAsync_ByACounterpartWrittenInAnotherCase_ReadsTheirTasks()
    {
        // Arrange
        var withThem = Task(TaskCommitment.Owed with { Counterpart = new TaskCounterpart(Address("Bea@example.org"), "Bea") });
        var withSomebodyElse = Task(TaskCommitment.Owed with { Counterpart = new TaskCounterpart(Address("carl@example.org"), null) });

        // Act
        var read = await ReadAsync([withThem, withSomebodyElse], new PersonalTaskQuery(null, Counterpart: Address("BEA@EXAMPLE.ORG")));

        // Assert
        Assert.Equal([withThem.Id], read.Select(task => task.Id));
    }

    [Fact]
    public async Task ReadAsync_AWindow_ReadsWhatIsDueOnItsDaysOrPlannedInsideIt()
    {
        // Arrange
        var due = Task(TaskCommitment.Owed, new DateOnly(2026, 11, 2));
        var planned = Task(TaskCommitment.Owed with { Plan = TaskPlan.Create(Opens.AddHours(10), 30) }, new DateOnly(2026, 11, 6));
        var dueTheDayTheWindowCloses = Task(TaskCommitment.Owed, new DateOnly(2026, 11, 3));
        var plannedAsTheWindowCloses = Task(TaskCommitment.Owed with { Plan = TaskPlan.Create(Opens.AddDays(1), 30) });

        // Act
        var read = await ReadAsync(
            [due, planned, dueTheDayTheWindowCloses, plannedAsTheWindowCloses],
            new PersonalTaskQuery(null, From: Opens, Until: Opens.AddDays(1)));

        // Assert
        Assert.Equal(new[] { due.Id, planned.Id }.Select(id => id.Value).Order(), read.Select(task => task.Id.Value).Order());
    }

    [Fact]
    public async Task SetCompletionAsync_ReopeningAnAnsweredWait_ClearsTheAnswer()
    {
        // Arrange
        var store = new InMemoryPersonalTaskStore();
        var waiting = Task(TaskCommitment.Owed with { Direction = PersonalTaskDirection.Awaited });
        await store.AddAsync(waiting, TestContext.Current.CancellationToken);
        await store.AnswerAsync(
            SyntheticUser.Deployment,
            waiting.Id,
            new TaskAnswer(StoredEmailId.Create(Guid.NewGuid()), Opens),
            TestContext.Current.CancellationToken);

        // Act
        var outcome = await store.SetCompletionAsync(SyntheticUser.Deployment, waiting.Id, isCompleted: false, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(PersonalTaskChangeOutcome.Applied, outcome);
        var reopened = Assert.Single(store.Held);
        Assert.False(reopened.IsCompleted);
        Assert.Null(reopened.Answer);
    }

    private static async Task<IReadOnlyList<PersonalTask>> ReadAsync(PersonalTask[] tasks, PersonalTaskQuery query)
    {
        var store = new InMemoryPersonalTaskStore();

        foreach (var task in tasks)
        {
            await store.AddAsync(task, TestContext.Current.CancellationToken);
        }

        return await store.ReadAsync(SyntheticUser.Deployment, query, after: null, Page, TestContext.Current.CancellationToken);
    }

    private static PersonalTask Task(TaskCommitment commitment, DateOnly? dueOn = null) => PersonalTask.Compose(
        PersonalTaskId.Create(Guid.NewGuid()),
        SyntheticUser.Deployment,
        "Send the figures",
        dueOn,
        TaskAnnouncement.Silent,
        PersonalTaskOrigin.Asserted,
        sourceMessage: null,
        commitment);

    private static EmailAddress Address(string address) =>
        EmailAddress.TryCreate(displayName: null, address, out var parsed)
            ? parsed
            : throw new InvalidOperationException("The fixture address is usable.");
}
