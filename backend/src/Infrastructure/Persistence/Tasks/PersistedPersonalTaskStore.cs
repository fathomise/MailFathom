// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Persistence;
using MailFathom.Application.Tasks;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Tasks;
using MailFathom.Infrastructure.Persistence.Emails.Threads;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MailFathom.Infrastructure.Persistence.Tasks;

/// <summary>Keeps what a person owes, in PostgreSQL.</summary>
/// <remarks>
/// <para>
/// Each write opens a session of its own rather than joining a caller's, because no caller has one: a task is written
/// by a person acting on a screen or by an enrichment run reporting on mail it has already stored, and composing the
/// task into that run's transaction would make a suggestion able to fail mail that was already committed.
/// </para>
/// <para>
/// The state changes a person makes run under the ordinary commit policy, so the read that finds the row and the
/// write that moves it are one transaction and a writer that got there first is a retry rather than a failure. None of
/// them can collide on a constraint: a task has no natural key, so two replicas moving one row converge on the same
/// state rather than racing for it. Answering a wait is the exception, because two replicas settling one reply must
/// not both report closing it, so it is one conditional update instead. The erasure uses neither, being a set-based
/// delete that composes with nothing a caller is holding.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this store.")]
[RequiresIntegrationCoverage]
internal sealed class PersistedPersonalTaskStore(
    MailFathomDbContext readContext,
    OptimisticConcurrencyRetryPolicy commitPolicy)
    : IPersonalTaskStore
{
    /// <inheritdoc />
    public Task AddAsync(PersonalTask task, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);

        return commitPolicy.CommitAsync(
            async (session, token) =>
            {
                var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, token);

                writeContext.PersonalTasks.Add(PersonalTaskMapping.ToEntity(task));
            },
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A read joins no transaction and takes no session, so it runs on the scoped context. The order is the order the
    /// index declares — one person's tasks, soonest due first, with the undated ones last and the identifier breaking
    /// a tie — which is also the order the list is drawn in.
    /// </remarks>
    public async Task<IReadOnlyList<PersonalTask>> ReadAsync(
        UserId user,
        PersonalTaskQuery query,
        PersonalTaskCursor? after,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        query.EnsureAnswerable();

        if (after is { Task.IsSpecified: false })
        {
            throw new ArgumentException(
                "A cursor continues a walk after a specified task, so the struct default names no position.",
                nameof(after));
        }

        var userValue = user.Value;
        var rows = readContext.PersonalTasks
            .AsNoTracking()
            .Include(task => task.Reminders)
            .Where(task => task.UserId == userValue);

        rows = Narrowed(rows, query);

        if (after is { } boundary)
        {
            rows = ReadBeyond(rows, boundary);
        }

        var page = await rows
            .OrderBy(task => task.DueOn)
            .ThenBy(task => task.Id)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

        return [.. page.Select(PersonalTaskMapping.ToPersonalTask)];
    }

    /// <inheritdoc />
    /// <remarks>
    /// A read joins no transaction and takes no session, so it runs on the scoped context, and the user is inside the
    /// predicate rather than checked after it.
    /// </remarks>
    public async Task<PersonalTask?> FindAsync(
        UserId user,
        PersonalTaskId task,
        CancellationToken cancellationToken)
    {
        var userValue = user.Value;
        var identifier = task.Value;

        var stored = await readContext.PersonalTasks
            .AsNoTracking()
            .Include(candidate => candidate.Reminders)
            .FirstOrDefaultAsync(
                candidate => candidate.UserId == userValue && candidate.Id == identifier,
                cancellationToken);

        return stored is null ? null : PersonalTaskMapping.ToPersonalTask(stored);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The columns an edit states and the reminder rows behind them, which is the only way this differs from
    /// the two state changes below it: the addressing, the session, and the commit policy are the same, and the
    /// revision supplies the row it is written against rather than being looked up from a second identity.
    /// <para>
    /// A reply answering the wait between the caller's read and this write leaves a row whose direction the revision
    /// would change under an answer, which the database refuses; that refusal is the answer the caller would have had
    /// if the reply had landed first, so it is reported as that rather than as a failed write.
    /// </para>
    /// </remarks>
    public async Task<PersonalTaskChangeOutcome> ReviseAsync(PersonalTask revision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(revision);

        try
        {
            return await commitPolicy.CommitAsync(
                (session, token) => StageAsync(
                    session,
                    revision.User,
                    revision.Id,
                    (writeContext, stored) =>
                    {
                        stored.Title = revision.Title;
                        stored.DueOn = revision.DueOn;
                        stored.DueDayOffsetMinutes = revision.DueDayOffset is { } offset
                            ? (int)offset.TotalMinutes
                            : null;
                        PersonalTaskMapping.WriteCommitment(stored, revision.Commitment);
                        stored.WaitingSince = revision.WaitingSince?.ToUniversalTime();

                        Reconcile(writeContext, stored, revision);
                    },
                    token),
                cancellationToken);
        }
        catch (Exception failure) when (ViolatesTheAnsweredWait(failure))
        {
            return PersonalTaskChangeOutcome.AnsweredWait;
        }
    }

    /// <summary>Reports whether a failure is the database refusing an answer on a task that is no longer a completed wait.</summary>
    internal static bool ViolatesTheAnsweredWait(Exception failure)
    {
        for (var current = failure; current is not null; current = current.InnerException)
        {
            if (current is PostgresException
                {
                    SqlState: PostgresErrorCodes.CheckViolation,
                    ConstraintName: PersistenceConstraintNames.PersonalTaskAnswerClosesAWaitCheckConstraintName,
                })
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Accepting is one column moving on the row that already exists, which is what keeps a proposal and the
    /// commitment it became one task. A task already asserted is left alone and reported as applied, so accepting
    /// twice writes once.
    /// </remarks>
    public Task<PersonalTaskChangeOutcome> AcceptAsync(
        UserId user,
        PersonalTaskId task,
        CancellationToken cancellationToken) =>
        commitPolicy.CommitAsync(
            (session, token) => StageAsync(
                session,
                user,
                task,
                (_, stored) => stored.Origin = PersonalTaskOrigin.Asserted,
                token),
            cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// The user is part of the lookup rather than a check after it, which is what makes another person's task answer
    /// as one that does not exist. Reopening a wait a reply answered clears that answer, because an open wait is one
    /// nothing has answered yet: the next reply settled in its thread answers it again. The wait is then held to begin
    /// when that reply arrived, because the person reopening it has said that reply was not the answer — so settling it
    /// again, as a resynchronization does, closes nothing.
    /// </remarks>
    public Task<PersonalTaskChangeOutcome> SetCompletionAsync(
        UserId user,
        PersonalTaskId task,
        bool isCompleted,
        CancellationToken cancellationToken) =>
        commitPolicy.CommitAsync(
            (session, token) => StageAsync(
                session,
                user,
                task,
                (_, stored) =>
                {
                    stored.IsCompleted = isCompleted;

                    if (!isCompleted)
                    {
                        stored.WaitingSince = stored.AnsweredAt ?? stored.WaitingSince;
                        stored.AnswerStoredEmailId = null;
                        stored.AnsweredAt = null;
                    }
                },
                token),
            cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// The merges are walked backwards from the thread a reply arrived in — each step one query for the threads folded
    /// into the ones already found — and the tasks are then read in one query over every identifier the walk reached.
    /// A merge points straight at its survivor, so the walk is one step on nearly every call and ends at the ceiling
    /// <see cref="SurvivingEmailThread" /> holds a forward walk to, against a chain that reached the database some
    /// other way.
    /// </remarks>
    public async Task<IReadOnlyList<PersonalTask>> ReadAwaitingAnswerAsync(
        EmailThreadId thread,
        CancellationToken cancellationToken)
    {
        var threads = await this.ThreadsEndingAtAsync(thread.Value, cancellationToken);

        var waiting = await readContext.PersonalTasks
            .AsNoTracking()
            .Include(task => task.Reminders)
            .Where(task => task.Direction == PersonalTaskDirection.Awaited
                && !task.IsCompleted
                && task.AnchorThreadId != null
                && threads.Contains(task.AnchorThreadId.Value))
            .OrderBy(task => task.Id)
            .ToArrayAsync(cancellationToken);

        return [.. waiting.Select(PersonalTaskMapping.ToPersonalTask)];
    }

    /// <inheritdoc />
    /// <remarks>
    /// One conditional update rather than a read and a write: the condition that the task is still an open wait is part
    /// of the statement, so PostgreSQL decides it under the row's lock. Two replicas settling one reply therefore write
    /// it once — the second matches no row and reports nothing to answer — and a wait the person completed by hand a
    /// moment earlier is left without an answer it never had. The wait having begun before the reply arrived is part of
    /// the condition too, so a person reopening the wait against this very reply between the read and the write is not
    /// overruled by it.
    /// </remarks>
    public async Task<PersonalTaskChangeOutcome> AnswerAsync(
        UserId user,
        PersonalTaskId task,
        TaskAnswer answer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(answer);

        var userValue = user.Value;
        var identifier = task.Value;
        var message = answer.Message.Value;
        var arrivedAt = answer.ArrivedAt.ToUniversalTime();

        var answered = await readContext.PersonalTasks
            .Where(candidate => candidate.UserId == userValue
                && candidate.Id == identifier
                && candidate.Direction == PersonalTaskDirection.Awaited
                && !candidate.IsCompleted
                && candidate.WaitingSince < arrivedAt)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(candidate => candidate.IsCompleted, true)
                    .SetProperty(candidate => candidate.AnswerStoredEmailId, message)
                    .SetProperty(candidate => candidate.AnsweredAt, arrivedAt),
                cancellationToken);

        return answered > 0 ? PersonalTaskChangeOutcome.Applied : PersonalTaskChangeOutcome.NotFound;
    }

    /// <inheritdoc />
    /// <remarks>
    /// One set-based delete rather than a lookup and a delete: the user is inside the predicate, so a task another
    /// person holds is never matched and never has to be told apart from one that does not exist.
    /// </remarks>
    public async Task<bool> EraseAsync(
        UserId user,
        PersonalTaskId task,
        CancellationToken cancellationToken)
    {
        var userValue = user.Value;
        var identifier = task.Value;

        var erased = await readContext.PersonalTasks
            .Where(candidate => candidate.UserId == userValue && candidate.Id == identifier)
            .ExecuteDeleteAsync(cancellationToken);

        return erased > 0;
    }

    /// <summary>Brings the held reminder rows to what the revision states, keeping what each one already announced.</summary>
    /// <remarks>
    /// Reconciled rather than replaced, because a row carries the claim that it has already been announced and
    /// deleting it to write it back would announce every reminder a second time on the next pass. A lead the
    /// revision keeps therefore keeps its row, and the claim on that row is cleared exactly when the instant the
    /// reminder falls at has moved — which is what makes a due date somebody moved reminded on its new day and a
    /// task merely renamed stay quiet.
    /// </remarks>
    private static void Reconcile(
        MailFathomDbContext writeContext,
        PersonalTaskEntity held,
        PersonalTask revision)
    {
        var stated = revision.Reminders.Select(reminder => reminder.MinutesBefore).ToHashSet();

        foreach (var dropped in held.Reminders.Where(row => !stated.Contains(row.MinutesBefore)).ToArray())
        {
            writeContext.PersonalTaskReminders.Remove(dropped);
        }

        foreach (var reminder in revision.Reminders)
        {
            var dueAt = revision.RemindsAt(reminder);
            var row = held.Reminders.FirstOrDefault(row => row.MinutesBefore == reminder.MinutesBefore);

            if (row is null)
            {
                held.Reminders.Add(PersonalTaskMapping.ToEntity(revision, reminder));
            }
            else if (row.DueAt != dueAt)
            {
                row.DueAt = dueAt;
                row.RaisedForDueAt = null;
            }
        }
    }

    /// <summary>Applies every narrowing a query states to one person's tasks.</summary>
    /// <remarks>
    /// The counterpart is compared by its normalized column, so the index answers it whatever case either address was
    /// written in. A window reads a task due on one of the days it reaches or planned to start inside it, which is the two places a
    /// calendar draws one.
    /// </remarks>
    private static IQueryable<PersonalTaskEntity> Narrowed(IQueryable<PersonalTaskEntity> rows, PersonalTaskQuery query)
    {
        if (query.Origin is { } origin)
        {
            rows = rows.Where(task => task.Origin == origin);
        }

        if (query.Thread is { } thread)
        {
            var threadValue = thread.Value;
            rows = rows.Where(task => task.AnchorThreadId == threadValue);
        }

        if (query.Event is { } calendarEvent)
        {
            var eventValue = calendarEvent.Value;
            rows = rows.Where(task => task.AnchorEventId == eventValue);
        }

        if (query.Counterpart is { } counterpart)
        {
            var normalized = counterpart.NormalizedAddress;
            rows = rows.Where(task => task.CounterpartNormalizedAddress == normalized);
        }

        if (query.From is { } from && query.Until is { } until && query.DueDays is { } days)
        {
            var (firstDay, lastDay) = days;
            var opensAt = from.ToUniversalTime();
            var closesAt = until.ToUniversalTime();
            rows = rows.Where(task => (task.DueOn >= firstDay && task.DueOn <= lastDay)
                || (task.PlannedStartAt >= opensAt && task.PlannedStartAt < closesAt));
        }

        return rows;
    }

    /// <summary>Collects a thread and every thread whose merges end at it.</summary>
    private async Task<IReadOnlyCollection<Guid>> ThreadsEndingAtAsync(Guid thread, CancellationToken cancellationToken)
    {
        var reached = new HashSet<Guid> { thread };
        Guid[] frontier = [thread];

        for (var step = 0; step < SurvivingEmailThread.MaximumMergeChainWalk && frontier.Length > 0; step++)
        {
            var current = frontier;
            var folded = await readContext.EmailThreads
                .AsNoTracking()
                .Where(candidate => candidate.MergedIntoEmailThreadId != null
                    && current.Contains(candidate.MergedIntoEmailThreadId.Value))
                .Select(candidate => candidate.Id)
                .ToArrayAsync(cancellationToken);

            frontier = [.. folded.Where(candidate => !reached.Contains(candidate))];
            reached.UnionWith(frontier);
        }

        return reached;
    }

    /// <summary>Narrows a person's tasks to the ones the order puts after a boundary.</summary>
    /// <remarks>
    /// The order is the day ascending with PostgreSQL's own <c>NULLS LAST</c> and the identifier breaking a tie, so
    /// continuing past a boundary is two cases rather than one. From a dated boundary the rest is every later day, the
    /// same day beyond that identifier, and the whole undated block that follows every date; a comparison against
    /// <c>NULL</c> yields <c>NULL</c> rather than true, which is why the undated block is named rather than left to
    /// the inequality. From an undated boundary the rest is the remainder of that block alone, because nothing in this
    /// order comes after it.
    /// </remarks>
    private static IQueryable<PersonalTaskEntity> ReadBeyond(
        IQueryable<PersonalTaskEntity> rows,
        PersonalTaskCursor boundary)
    {
        var boundaryId = boundary.Task.Value;

        if (boundary.DueOn is not { } boundaryDueOn)
        {
            return rows.Where(task => task.DueOn == null && task.Id > boundaryId);
        }

        return rows.Where(task => task.DueOn == null
            || task.DueOn > boundaryDueOn
            || (task.DueOn == boundaryDueOn && task.Id > boundaryId));
    }

    /// <summary>Finds one of a person's tasks inside the session and applies a change to it.</summary>
    /// <remarks>
    /// Every state change here has the same two halves — address the row through the user as well as the identifier,
    /// then move what the change states — so the addressing is written once and each caller supplies only that.
    /// Assigning a value the row already carries leaves the change tracker with nothing to write, which is what makes
    /// a repeated request a read rather than a second write. The context is handed to the change because a revision
    /// removes reminder rows as well as moving columns, and the two changes that move one column ignore it.
    /// </remarks>
    private static async Task<PersonalTaskChangeOutcome> StageAsync(
        IPersistenceSession session,
        UserId user,
        PersonalTaskId task,
        Action<MailFathomDbContext, PersonalTaskEntity> change,
        CancellationToken cancellationToken)
    {
        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var userValue = user.Value;
        var identifier = task.Value;

        var stored = await writeContext.PersonalTasks
            .Include(candidate => candidate.Reminders)
            .FirstOrDefaultAsync(
                candidate => candidate.UserId == userValue && candidate.Id == identifier,
                cancellationToken);

        if (stored is null)
        {
            return PersonalTaskChangeOutcome.NotFound;
        }

        change(writeContext, stored);

        return PersonalTaskChangeOutcome.Applied;
    }
}
