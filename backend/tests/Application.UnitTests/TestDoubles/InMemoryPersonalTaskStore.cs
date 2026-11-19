// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Tasks;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Tasks;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Holds what a person owes in memory, ordered and scoped the way the persisted store is.</summary>
/// <remarks>
/// The order and the keyset boundary are reproduced rather than approximated, because they are what the use case above
/// reads a page against: a double that ordered by insertion would pass every paging test while the real store served a
/// different list. What it does not reproduce is the transaction, which is the persisted store's own subject.
/// </remarks>
internal sealed class InMemoryPersonalTaskStore : IPersonalTaskStore
{
    private readonly List<PersonalTask> tasks = [];

    /// <summary>Gets every task the store holds, for a test asserting what a write left behind.</summary>
    public IReadOnlyList<PersonalTask> Held => this.tasks;

    /// <summary>Gets the limit the last read was asked for, which is what a clamp is observable through.</summary>
    public int LastLimit { get; private set; }

    public Task AddAsync(PersonalTask task, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);

        this.tasks.Add(task);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PersonalTask>> ReadAsync(
        UserId user,
        PersonalTaskQuery query,
        PersonalTaskCursor? after,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.EnsureAnswerable();

        this.LastLimit = limit;

        var ordered = this.tasks
            .Where(task => task.User == user && Matches(task, query))
            .OrderBy(task => task.DueOn is null)
            .ThenBy(task => task.DueOn)
            .ThenBy(task => task.Id.Value);

        var page = after is { } boundary
            ? ordered.Where(task => IsBeyond(task, boundary))
            : ordered;

        return Task.FromResult<IReadOnlyList<PersonalTask>>([.. page.Take(limit)]);
    }

    /// <summary>Reads the open waits anchored to one thread, whoever holds them.</summary>
    /// <remarks>Threads are never merged here, so a wait is answered in the thread it was anchored to and no other.</remarks>
    public Task<IReadOnlyList<PersonalTask>> ReadAwaitingAnswerAsync(EmailThreadId thread, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PersonalTask>>(
        [
            .. this.tasks.Where(task => task.Direction == PersonalTaskDirection.Awaited
                && !task.IsCompleted
                && task.Anchor?.ThreadId == thread),
        ]);

    public Task<PersonalTaskChangeOutcome> AnswerAsync(
        UserId user,
        PersonalTaskId task,
        TaskAnswer answer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(answer);

        var held = this.tasks.FirstOrDefault(candidate => candidate.User == user && candidate.Id == task);

        return Task.FromResult(held is { Direction: PersonalTaskDirection.Awaited, IsCompleted: false }
            ? this.Replace(user, task, open => open.Answered(answer))
            : PersonalTaskChangeOutcome.NotFound);
    }

    public Task<PersonalTask?> FindAsync(UserId user, PersonalTaskId task, CancellationToken cancellationToken) =>
        Task.FromResult(this.tasks.FirstOrDefault(held => held.User == user && held.Id == task));

    public Task<PersonalTaskChangeOutcome> ReviseAsync(PersonalTask revision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(revision);

        if (this.tasks.Any(held => held.Id == revision.Id && held.Answer is not null && held.Direction != revision.Direction))
        {
            return Task.FromResult(PersonalTaskChangeOutcome.AnsweredWait);
        }

        return Task.FromResult(this.Replace(
            revision.User,
            revision.Id,
            held => held.Revise(revision.Title, revision.DueOn, Announcing(revision), revision.Commitment)));
    }

    public Task<PersonalTaskChangeOutcome> AcceptAsync(
        UserId user,
        PersonalTaskId task,
        CancellationToken cancellationToken) =>
        Task.FromResult(this.Replace(
            user,
            task,
            held => PersonalTask.Restore(
                held.Id,
                held.User,
                held.Title,
                held.DueOn,
                Announcing(held),
                PersonalTaskOrigin.Asserted,
                held.SourceMessage,
                held.IsCompleted,
                held.Commitment,
                held.Answer)));

    public Task<PersonalTaskChangeOutcome> SetCompletionAsync(
        UserId user,
        PersonalTaskId task,
        bool isCompleted,
        CancellationToken cancellationToken) =>
        Task.FromResult(this.Replace(
            user,
            task,
            held => PersonalTask.Restore(
                held.Id,
                held.User,
                held.Title,
                held.DueOn,
                Announcing(held),
                held.Origin,
                held.SourceMessage,
                isCompleted,
                held.Commitment,
                isCompleted ? held.Answer : null)));

    /// <summary>Restates what announces a held task, which a state change keeps rather than rewrites.</summary>
    private static TaskAnnouncement Announcing(PersonalTask task) => task.DueDayOffset is { } offset
        ? new TaskAnnouncement(offset, task.Reminders)
        : TaskAnnouncement.Silent;

    public Task<bool> EraseAsync(UserId user, PersonalTaskId task, CancellationToken cancellationToken) =>
        Task.FromResult(this.tasks.RemoveAll(held => held.User == user && held.Id == task) > 0);

    /// <summary>Reports whether a task meets every narrowing a query states, as the persisted store reads them.</summary>
    private static bool Matches(PersonalTask task, PersonalTaskQuery query) =>
        (query.Origin is not { } origin || task.Origin == origin)
        && (query.Thread is not { } thread || task.Anchor?.ThreadId == thread)
        && (query.Event is not { } calendarEvent || task.Anchor?.EventId == calendarEvent)
        && (query.Counterpart is not { } counterpart || task.Counterpart?.Address == counterpart)
        && (query.DueDays is not { } days
            || (task.DueOn >= days.First && task.DueOn <= days.Last)
            || (task.Plan?.StartAt >= query.From && task.Plan?.StartAt < query.Until));

    /// <summary>Reports whether a task stands after a boundary in the order this store serves.</summary>
    private static bool IsBeyond(PersonalTask task, PersonalTaskCursor boundary) => boundary.DueOn is { } dueOn
        ? task.DueOn is null || task.DueOn > dueOn || (task.DueOn == dueOn && task.Id.Value > boundary.Task.Value)
        : task.DueOn is null && task.Id.Value > boundary.Task.Value;

    /// <summary>Applies a change to one of a person's tasks, reporting what became of the request.</summary>
    private PersonalTaskChangeOutcome Replace(
        UserId user,
        PersonalTaskId task,
        Func<PersonalTask, PersonalTask> change)
    {
        var position = this.tasks.FindIndex(held => held.User == user && held.Id == task);

        if (position < 0)
        {
            return PersonalTaskChangeOutcome.NotFound;
        }

        this.tasks[position] = change(this.tasks[position]);

        return PersonalTaskChangeOutcome.Applied;
    }
}
