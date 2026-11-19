// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Tasks;
using MailFathom.Domain.Calendar;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Tasks;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves against real PostgreSQL that a task's commitment is stored whole and read by what surfaces ask.</summary>
/// <remarks>
/// The row shape and the model are the unit suite's; what only this suite settles is that the narrowings translate —
/// a window read as a day range or a plan, an anchor, a merge chain walked through <c>email_threads</c> — and that an
/// answer is decided inside the write. Every case reads the served user's list, so each one works in a window and on
/// threads of its own rather than on anything another case wrote.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedPersonalTaskStoreTests(MailFathomOrchestrationFixture orchestration)
{
    private const int Page = 50;

    private static readonly TimeSpan Warsaw = TimeSpan.FromHours(1);

    private static readonly DateTimeOffset WrittenAt = new(2031, 1, 2, 8, 0, 0, Warsaw);

    [Fact]
    public async Task AddAsync_ATaskStatingEveryPartOfItsCommitment_ReadsBackWhole()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var commitment = new TaskCommitment(
            PersonalTaskDirection.Awaited,
            EstimatedMinutes: 45,
            TaskPlan.Create(new DateTimeOffset(2031, 2, 3, 14, 0, 0, Warsaw), 30),
            TaskAnchor.Thread(EmailThreadId.Create(Guid.NewGuid()), "Tender reply"),
            new TaskCounterpart(Address("Anna.Example@example.org"), "Anna Example"));
        var task = Task(services, "Hear back about the tender", new DateOnly(2031, 2, 4), commitment);

        // Act
        var read = await InStoreAsync(
            services,
            async (store, token) =>
            {
                await store.AddAsync(task, token);

                return await store.FindAsync(services.ServedUser, task.Id, token);
            },
            cancellationToken);

        // Assert
        Assert.NotNull(read);
        Assert.Equal(commitment, read.Commitment);
        Assert.Equal(WrittenAt, read.WaitingSince);
        Assert.Null(read.Answer);
    }

    [Fact]
    public async Task ReviseAsync_ANewCommitment_IsWrittenAndTheOldOneLeavesNothingBehind()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var held = Task(
            services,
            "Bring the slides",
            dueOn: null,
            TaskCommitment.Owed with
            {
                Plan = TaskPlan.Create(new DateTimeOffset(2031, 2, 10, 9, 0, 0, Warsaw), 60),
                Anchor = TaskAnchor.Thread(EmailThreadId.Create(Guid.NewGuid()), "Slides"),
            });
        var commitment = TaskCommitment.Owed with
        {
            EstimatedMinutes = 15,
            Anchor = TaskAnchor.Event(CalendarEventId.Create(Guid.NewGuid()), "Kick-off"),
        };

        // Act
        var read = await InStoreAsync(
            services,
            async (store, token) =>
            {
                await store.AddAsync(held, token);
                Assert.Equal(
                    PersonalTaskChangeOutcome.Applied,
                    await store.ReviseAsync(held.Revise(held.Title, held.DueOn, TaskAnnouncement.Silent, commitment, DateTimeOffset.UnixEpoch), token));

                return await store.FindAsync(services.ServedUser, held.Id, token);
            },
            cancellationToken);

        // Assert
        Assert.NotNull(read);
        Assert.Equal(commitment, read.Commitment);
    }

    [Fact]
    public async Task ReadAsync_ATaskPlannedInsideTheWindowButDueAfterIt_IsRead()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var from = new DateTimeOffset(2031, 3, 3, 0, 0, 0, Warsaw);
        var planned = Task(
            services,
            "Draft the reply",
            new DateOnly(2031, 3, 6),
            TaskCommitment.Owed with { Plan = TaskPlan.Create(from.AddHours(10), 30) });
        var outside = Task(
            services,
            "Draft the minutes",
            new DateOnly(2031, 3, 6),
            TaskCommitment.Owed with { Plan = TaskPlan.Create(from.AddDays(1), 30) });

        // Act
        var read = await AddAndReadAsync(services, [planned, outside], new PersonalTaskQuery(null, From: from, Until: from.AddDays(1)), cancellationToken);

        // Assert
        Assert.Equal([planned.Id], read.Select(task => task.Id));
    }

    [Fact]
    public async Task ReadAsync_ATaskDueInsideTheWindowButPlannedAfterIt_IsRead()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var from = new DateTimeOffset(2031, 4, 7, 0, 0, 0, Warsaw);
        var due = Task(
            services,
            "Send the counter-proposal",
            new DateOnly(2031, 4, 7),
            TaskCommitment.Owed with { Plan = TaskPlan.Create(from.AddDays(2), 30) });
        var dueTheDayTheWindowCloses = Task(services, "Send the invoice", new DateOnly(2031, 4, 8), TaskCommitment.Owed);

        // Act
        var read = await AddAndReadAsync(services, [due, dueTheDayTheWindowCloses], new PersonalTaskQuery(null, From: from, Until: from.AddDays(1)), cancellationToken);

        // Assert
        Assert.Equal([due.Id], read.Select(task => task.Id));
    }

    [Fact]
    public async Task ReadAsync_ByThread_ReadsOnlyThatThreadsTasks()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var thread = EmailThreadId.Create(Guid.NewGuid());
        var anchored = Task(services, "Answer the tender", dueOn: null, TaskCommitment.Owed with { Anchor = TaskAnchor.Thread(thread, "Tender") });
        var elsewhere = Task(
            services,
            "Answer the auditor",
            dueOn: null,
            TaskCommitment.Owed with { Anchor = TaskAnchor.Thread(EmailThreadId.Create(Guid.NewGuid()), "Audit") });

        // Act
        var read = await AddAndReadAsync(services, [anchored, elsewhere], new PersonalTaskQuery(null, Thread: thread), cancellationToken);

        // Assert
        Assert.Equal([anchored.Id], read.Select(task => task.Id));
    }

    [Fact]
    public async Task ReadAsync_ByACounterpartWrittenInAnotherCase_ReadsTheirTasksAsWritten()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var written = $"Bea.{Guid.NewGuid():N}@example.org";
        var withThem = Task(
            services,
            "Send Bea the figures",
            dueOn: null,
            TaskCommitment.Owed with { Counterpart = new TaskCounterpart(Address(written), "Bea Example") });
        var withSomebodyElse = Task(
            services,
            "Send Carl the figures",
            dueOn: null,
            TaskCommitment.Owed with { Counterpart = new TaskCounterpart(Address($"carl.{Guid.NewGuid():N}@example.org"), null) });

        // Act
        var read = await AddAndReadAsync(
            services,
            [withThem, withSomebodyElse],
            new PersonalTaskQuery(null, Counterpart: Address(written.ToUpperInvariant())),
            cancellationToken);

        // Assert
        var task = Assert.Single(read);
        Assert.Equal(withThem.Id, task.Id);
        Assert.Equal(written, task.Counterpart?.Address.Address);
    }

    [Fact]
    public async Task ReadAwaitingAnswerAsync_ATaskAnchoredToAMergedAwayThread_IsRead()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var surviving = Guid.NewGuid();
        var mergedAway = Guid.NewGuid();
        var mergedBeforeThat = Guid.NewGuid();
        await SeedMergedThreadsAsync(services, surviving, mergedAway, mergedBeforeThat, cancellationToken);
        var awaitingSurvivor = Awaiting(services, surviving);
        var awaitingMerged = Awaiting(services, mergedAway);
        var awaitingTwoMergesBack = Awaiting(services, mergedBeforeThat);
        var awaitingElsewhere = Awaiting(services, Guid.NewGuid());
        var owedInTheThread = Task(
            services,
            "Thank them for the reply",
            dueOn: null,
            TaskCommitment.Owed with { Anchor = TaskAnchor.Thread(EmailThreadId.Create(surviving), "Tender") });

        // Act
        var read = await InStoreAsync(
            services,
            async (store, token) =>
            {
                foreach (var task in new[] { awaitingSurvivor, awaitingMerged, awaitingTwoMergesBack, awaitingElsewhere, owedInTheThread })
                {
                    await store.AddAsync(task, token);
                }

                return await store.ReadAwaitingAnswerAsync(EmailThreadId.Create(surviving), token);
            },
            cancellationToken);

        // Assert
        Assert.Equal(
            new[] { awaitingSurvivor.Id, awaitingMerged.Id, awaitingTwoMergesBack.Id }.Select(task => task.Value).Order(),
            read.Select(task => task.Id.Value).Order());
    }

    [Fact]
    public async Task AnswerAsync_AnAlreadyCompletedTask_ChangesNothing()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var waiting = Awaiting(services, Guid.NewGuid());
        var reply = new TaskAnswer(StoredEmailId.Create(Guid.NewGuid()), new DateTimeOffset(2031, 5, 2, 9, 0, 0, Warsaw));

        // Act
        var (outcome, read) = await InStoreAsync(
            services,
            async (store, token) =>
            {
                await store.AddAsync(waiting, token);
                await store.SetCompletionAsync(services.ServedUser, waiting.Id, isCompleted: true, token);

                var answered = await store.AnswerAsync(services.ServedUser, waiting.Id, reply, token);

                return (answered, await store.FindAsync(services.ServedUser, waiting.Id, token));
            },
            cancellationToken);

        // Assert
        Assert.Equal(PersonalTaskChangeOutcome.NotFound, outcome);
        Assert.NotNull(read);
        Assert.True(read.IsCompleted);
        Assert.Null(read.Answer);
    }

    [Fact]
    public async Task AnswerAsync_AnOpenWait_ClosesItCitingTheReply()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var waiting = Awaiting(services, Guid.NewGuid());
        var reply = new TaskAnswer(StoredEmailId.Create(Guid.NewGuid()), new DateTimeOffset(2031, 5, 3, 9, 0, 0, Warsaw));

        // Act
        var (outcome, read) = await InStoreAsync(
            services,
            async (store, token) =>
            {
                await store.AddAsync(waiting, token);

                var answered = await store.AnswerAsync(services.ServedUser, waiting.Id, reply, token);

                return (answered, await store.FindAsync(services.ServedUser, waiting.Id, token));
            },
            cancellationToken);

        // Assert
        Assert.Equal(PersonalTaskChangeOutcome.Applied, outcome);
        Assert.NotNull(read);
        Assert.True(read.IsCompleted);
        Assert.Equal(reply, read.Answer);
    }

    [Fact]
    public async Task AnswerAsync_TheReplyAWaitWasReopenedAgainst_ChangesNothing()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var waiting = Awaiting(services, Guid.NewGuid());
        var reply = new TaskAnswer(StoredEmailId.Create(Guid.NewGuid()), new DateTimeOffset(2031, 5, 7, 9, 0, 0, Warsaw));
        var before = new TaskAnswer(StoredEmailId.Create(Guid.NewGuid()), WrittenAt.AddMinutes(-1));

        // Act
        var (outcomes, read) = await InStoreAsync(
            services,
            async (store, token) =>
            {
                await store.AddAsync(waiting, token);
                await store.AnswerAsync(services.ServedUser, waiting.Id, reply, token);
                await store.SetCompletionAsync(services.ServedUser, waiting.Id, isCompleted: false, token);

                PersonalTaskChangeOutcome[] answered =
                [
                    await store.AnswerAsync(services.ServedUser, waiting.Id, reply, token),
                    await store.AnswerAsync(services.ServedUser, waiting.Id, before, token),
                ];

                return (answered, await store.FindAsync(services.ServedUser, waiting.Id, token));
            },
            cancellationToken);

        // Assert
        Assert.Equal([PersonalTaskChangeOutcome.NotFound, PersonalTaskChangeOutcome.NotFound], outcomes);
        Assert.NotNull(read);
        Assert.False(read.IsCompleted);
        Assert.Null(read.Answer);
    }

    [Fact]
    public async Task SetCompletionAsync_ReopeningAnAnsweredWait_ClearsTheAnswerAndTheListStillReads()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var thread = Guid.NewGuid();
        var waiting = Awaiting(services, thread);
        var reply = new TaskAnswer(StoredEmailId.Create(Guid.NewGuid()), new DateTimeOffset(2031, 5, 5, 9, 0, 0, Warsaw));

        // Act
        var (read, awaiting) = await InStoreAsync(
            services,
            async (store, token) =>
            {
                await store.AddAsync(waiting, token);
                await store.AnswerAsync(services.ServedUser, waiting.Id, reply, token);
                Assert.Equal(
                    PersonalTaskChangeOutcome.Applied,
                    await store.SetCompletionAsync(services.ServedUser, waiting.Id, isCompleted: false, token));

                return (
                    await store.FindAsync(services.ServedUser, waiting.Id, token),
                    await store.ReadAwaitingAnswerAsync(EmailThreadId.Create(thread), token));
            },
            cancellationToken);

        // Assert
        Assert.NotNull(read);
        Assert.False(read.IsCompleted);
        Assert.Null(read.Answer);
        Assert.Equal(reply.ArrivedAt, read.WaitingSince);
        Assert.Equal([waiting.Id], awaiting.Select(task => task.Id));
    }

    [Fact]
    public async Task Table_AnAnswerOnAWaitStillOpen_IsRefused()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var waiting = Awaiting(services, Guid.NewGuid());
        await InStoreAsync(
            services,
            async (store, token) =>
            {
                await store.AddAsync(waiting, token);

                return true;
            },
            cancellationToken);

        // Act
        var refusal = await services.InScopeAsync(
            (scope, token) => Assert.ThrowsAsync<PostgresException>(() => scope.GetRequiredService<MailFathomDbContext>()
                .PersonalTasks
                .Where(task => task.Id == waiting.Id.Value)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(task => task.AnswerStoredEmailId, Guid.NewGuid())
                        .SetProperty(task => task.AnsweredAt, new DateTimeOffset(2031, 5, 6, 9, 0, 0, TimeSpan.Zero)),
                    token)),
            cancellationToken);

        // Assert
        Assert.Equal(PersistenceConstraintNames.PersonalTaskAnswerClosesAWaitCheckConstraintName, refusal.ConstraintName);
    }

    [Theory]
    [InlineData("UPDATE tasks SET \"Direction\" = 'Sideways' WHERE \"Id\" = {0}", false, PersistenceConstraintNames.PersonalTaskDirectionDeclaredCheckConstraintName)]
    [InlineData("UPDATE tasks SET \"EstimatedMinutes\" = 721 WHERE \"Id\" = {0}", false, PersistenceConstraintNames.PersonalTaskEstimateBoundedCheckConstraintName)]
    [InlineData("UPDATE tasks SET \"PlannedStartAt\" = now(), \"PlannedMinutes\" = 4 WHERE \"Id\" = {0}", false, PersistenceConstraintNames.PersonalTaskPlanBoundedCheckConstraintName)]
    [InlineData("UPDATE tasks SET \"WaitingSince\" = NULL WHERE \"Id\" = {0}", true, PersistenceConstraintNames.PersonalTaskWaitBeganCheckConstraintName)]
    [InlineData("UPDATE tasks SET \"WaitingSince\" = now() WHERE \"Id\" = {0}", false, PersistenceConstraintNames.PersonalTaskWaitBeganCheckConstraintName)]
    public async Task Table_AValueTheDomainRefuses_IsRefused(string statement, bool waits, string constraint)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var task = waits ? Awaiting(services, Guid.NewGuid()) : Task(services, "Book the room", dueOn: null, TaskCommitment.Owed);
        await InStoreAsync(
            services,
            async (store, token) =>
            {
                await store.AddAsync(task, token);

                return true;
            },
            cancellationToken);

        // Act
        var refusal = await services.InScopeAsync(
            (scope, token) => Assert.ThrowsAsync<PostgresException>(() => scope.GetRequiredService<MailFathomDbContext>()
                .Database
                .ExecuteSqlRawAsync(statement, [task.Id.Value], token)),
            cancellationToken);

        // Assert
        Assert.Equal(constraint, refusal.ConstraintName);
    }

    private static Task<IReadOnlyList<PersonalTask>> AddAndReadAsync(
        OrchestratedMailFathomServices services,
        PersonalTask[] tasks,
        PersonalTaskQuery query,
        CancellationToken cancellationToken) => InStoreAsync(
        async (store, token) =>
        {
            foreach (var task in tasks)
            {
                await store.AddAsync(task, token);
            }

            return await store.ReadAsync(services.ServedUser, query, after: null, Page, token);
        },
        services,
        cancellationToken);

    private static Task<TResult> InStoreAsync<TResult>(
        OrchestratedMailFathomServices services,
        Func<IPersonalTaskStore, CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken) => InStoreAsync(work, services, cancellationToken);

    private static Task<TResult> InStoreAsync<TResult>(
        Func<IPersonalTaskStore, CancellationToken, Task<TResult>> work,
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
        (scope, token) => work(scope.GetRequiredService<IPersonalTaskStore>(), token),
        cancellationToken);

    private static Task<bool> SeedMergedThreadsAsync(
        OrchestratedMailFathomServices services,
        Guid surviving,
        Guid mergedAway,
        Guid mergedBeforeThat,
        CancellationToken cancellationToken) => services.InScopeAsync(
        async (scope, token) =>
        {
            var context = scope.GetRequiredService<MailFathomDbContext>();
            var account = $"personal-tasks-{surviving:N}";

            context.MailboxAccounts.Add(new MailboxAccountEntity { Id = account });
            context.EmailThreads.AddRange(
                Thread(surviving, account, mergedInto: null),
                Thread(mergedAway, account, surviving),
                Thread(mergedBeforeThat, account, mergedAway));
            await context.SaveChangesAsync(token);

            return true;
        },
        cancellationToken);

    private static EmailThreadEntity Thread(Guid id, string account, Guid? mergedInto) => new()
    {
        Id = id,
        MailboxAccountId = account,
        AssembledAt = new DateTimeOffset(2031, 1, 1, 0, 0, 0, TimeSpan.Zero),
        MergedIntoEmailThreadId = mergedInto,
    };

    private static PersonalTask Awaiting(OrchestratedMailFathomServices services, Guid thread) => Task(
        services,
        "Hear back about the tender",
        dueOn: null,
        TaskCommitment.Owed with
        {
            Direction = PersonalTaskDirection.Awaited,
            Anchor = TaskAnchor.Thread(EmailThreadId.Create(thread), "Tender"),
        });

    private static PersonalTask Task(
        OrchestratedMailFathomServices services,
        string title,
        DateOnly? dueOn,
        TaskCommitment commitment) => PersonalTask.Compose(
        PersonalTaskId.Create(Guid.CreateVersion7()),
        services.ServedUser,
        title,
        dueOn,
        TaskAnnouncement.Silent,
        PersonalTaskOrigin.Asserted,
        sourceMessage: null,
        commitment,
        WrittenAt);

    private static EmailAddress Address(string address) =>
        EmailAddress.TryCreate(displayName: null, address, out var parsed)
            ? parsed
            : throw new InvalidOperationException("The fixture address is usable.");
}
