// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using MailFathom.Application.Access;
using MailFathom.Application.Paging;
using MailFathom.Application.Signals;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Reminders;
using MailFathom.Domain.Tasks;

namespace MailFathom.Application.Tasks;

/// <summary>Reads and writes the signed-in person's own task list.</summary>
/// <remarks>
/// <para>
/// Whose tasks these are comes from the principal rather than from the request, exactly as it does for the user record,
/// the client preferences, and the notification centre: there is no argument here for another user's identifier, so a
/// reading of somebody else's list is something a caller cannot express rather than something a surface has to refuse.
/// A task named by identifier is addressed with the user beside it, so one another person holds answers as one that
/// does not exist.
/// </para>
/// <para>
/// The two origins are read apart rather than filtered, because they are two things to somebody looking at a screen:
/// what they have committed to, and what mail suggested and nobody has agreed to yet. A cursor is issued against the
/// whole narrowing it was read under — the origin, and the thread, event, person, or window a surface asked for — and
/// refused under any other, which is what stops a walk of the proposals continuing into the commitments halfway down a
/// screen.
/// </para>
/// <para>
/// The reads are admitted under <see cref="MailFathomPermission.TasksRead" /> and the writes under
/// <see cref="MailFathomPermission.TasksWrite" />, neither of which is a grant over mail: a task is this deployment's
/// own record of what a person owes, nothing here reaches a mail server, nothing moves in a mailbox, and a person
/// whose mail accounts an administrator maintains keeps their own list under a grant that says nothing about those
/// accounts.
/// </para>
/// <para>
/// A write that changed the list is announced to the person's open clients once it is durable, so a second device
/// draws it without being reopened.
/// </para>
/// </remarks>
public sealed class OwnTasks
{
    /// <summary>The page size a request that names none is served.</summary>
    /// <remarks>Enough to fill the list a client draws without scrolling, so a first paint is one request.</remarks>
    public const int DefaultPageSize = 50;

    /// <summary>The greatest page size one request is served, whatever it asked for.</summary>
    /// <remarks>
    /// A task is a line, a day, and two flags, so a page of this many is a few tens of kilobytes. It is a bound rather
    /// than a refusal, for the reason the notification centre clamps where the mail list refuses: this serves a panel
    /// asking for as much as it can draw, where the useful answer to a number no screen wants is the most this
    /// deployment serves rather than an error the screen has to render instead of a list.
    /// </remarks>
    public const int MaximumPageSize = 200;

    private readonly AccessAuthorization authorization;
    private readonly IPersonalTaskStore store;
    private readonly TimeProvider clock;
    private readonly ClientSignals signals;

    /// <summary>Initializes the use case.</summary>
    /// <param name="authorization">Reports the grant the caller holds and the user it acts for.</param>
    /// <param name="store">Holds what a person owes.</param>
    /// <param name="clock">Stamps the identity a new task is addressed under, and when a wait begins.</param>
    /// <param name="signals">Announces a durable write to the person's open clients.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public OwnTasks(
        AccessAuthorization authorization,
        IPersonalTaskStore store,
        TimeProvider clock,
        ClientSignals signals)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(signals);

        this.authorization = authorization;
        this.store = store;
        this.clock = clock;
        this.signals = signals;
    }

    /// <summary>Reads one page of the signed-in person's tasks, narrowed as the query states, soonest due first.</summary>
    /// <param name="query">What the list is narrowed to: the half of it to read, and the thread, event, person, or window a surface draws.</param>
    /// <param name="pageSize">How many tasks the page may hold, or <see langword="null" /> for <see cref="DefaultPageSize" />; a larger number is served <see cref="MaximumPageSize" />.</param>
    /// <param name="cursor">The cursor a previous page returned, or <see langword="null" /> for the first page.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The page, or <see langword="null" /> when the cursor is not one this deployment issued for this reading.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="query" /> is one <see cref="PersonalTaskQuery.EnsureAnswerable" /> refuses.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.TasksRead" />.</exception>
    /// <remarks>
    /// Completed tasks are served with the outstanding ones and carry the flag that tells them apart, because which of
    /// them a screen draws is the screen's decision: a list that hid them would leave a person no way to see what they
    /// did today, and one filtered here would need a second route to get them back.
    /// </remarks>
    public async Task<PersonalTaskPage?> ReadPageAsync(
        PersonalTaskQuery query,
        int? pageSize,
        string? cursor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.EnsureAnswerable();

        this.authorization.RequirePermission(MailFathomPermission.TasksRead);

        var user = this.authorization.RequireUser();
        var fingerprint = FingerprintOf(user, query);
        PersonalTaskCursor? boundary = null;

        if (!string.IsNullOrWhiteSpace(cursor) && !TryReadBoundary(cursor, fingerprint, out boundary))
        {
            return null;
        }

        var limit = Bounded(pageSize);
        var tasks = await this.store.ReadAsync(user, query, boundary, limit, cancellationToken);

        // The page is short only where the list held nothing more, so the boundary is issued exactly when a full page
        // came back — which is what lets a caller stop on the absent cursor rather than on a length comparison.
        var next = tasks.Count == limit && tasks[^1] is { } last
            ? Encode(last, fingerprint)
            : null;

        return new PersonalTaskPage(tasks, next);
    }

    /// <summary>Reads one of the signed-in person's tasks.</summary>
    /// <param name="task">The task to read.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The task, or <see langword="null" /> when this person holds none under that identity.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.TasksRead" />.</exception>
    public Task<PersonalTask?> FindAsync(PersonalTaskId task, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.TasksRead);

        return this.store.FindAsync(this.authorization.RequireUser(), task, cancellationToken);
    }

    /// <summary>Records a task the signed-in person has just committed to.</summary>
    /// <param name="title">The line the list is drawn with.</param>
    /// <param name="dueOn">The day it is due on, or <see langword="null" /> where they said nothing about when.</param>
    /// <param name="announcement">What announces it and the offset their due day runs in, or <see cref="TaskAnnouncement.Silent" /> to announce nothing.</param>
    /// <param name="commitment">Whether they owe it or wait on it, and the estimate, plan, anchor, and counterpart that go with it.</param>
    /// <param name="sourceMessage">The message they were looking at, or <see langword="null" /> where the task cites none.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The task as it was written.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="announcement" /> or <paramref name="commitment" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="title" /> is blank, when one lead is stated twice, or when leads are stated against no due day.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="title" /> exceeds <see cref="PersonalTask.MaximumTitleLength" />, or when more than <see cref="Reminder.MaximumCount" /> leads are stated.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.TasksWrite" />.</exception>
    /// <remarks>
    /// What somebody types into their own client is something they owe, so a task written here is asserted and this is
    /// not the seam a proposal arrives through: what mail suggested is written by the extraction that read it, under
    /// the origin that says nobody has agreed to it yet.
    /// <para>
    /// The citation is taken as the caller states it and is not resolved against the mailbox. It is a value the task
    /// keeps rather than a reference the row depends on, and following one is the citation route's act under the same
    /// grant — so an identity naming nothing resolves to nothing there rather than being refused here, and nothing about
    /// whose mail exists is reported by writing a task.
    /// </para>
    /// </remarks>
    public async Task<PersonalTask> RecordAsync(
        string title,
        DateOnly? dueOn,
        TaskAnnouncement announcement,
        TaskCommitment commitment,
        StoredEmailId? sourceMessage,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.TasksWrite);

        var now = this.clock.GetUtcNow();
        var task = PersonalTask.Compose(
            PersonalTaskId.Create(Guid.CreateVersion7(now)),
            this.authorization.RequireUser(),
            title,
            dueOn,
            announcement,
            PersonalTaskOrigin.Asserted,
            sourceMessage,
            commitment,
            now);

        await this.store.AddAsync(task, cancellationToken);

        this.signals.Publish(ClientSignal.TasksChanged(task.User));

        return task;
    }

    /// <summary>Writes what the signed-in person edited about one of their tasks.</summary>
    /// <param name="task">The task to revise.</param>
    /// <param name="title">The line the list is to be drawn with from now on.</param>
    /// <param name="dueOn">The day it is due on, or <see langword="null" /> where they took the date off it.</param>
    /// <param name="announcement">What announces it afterwards and the offset their due day runs in, or <see cref="TaskAnnouncement.Silent" /> to announce nothing.</param>
    /// <param name="commitment">The commitment as it is to stand from now on, which replaces the one the task held.</param>
    /// <param name="opensAnchor">Says whether the person can open what an anchor names, asked only of an anchor the task does not already hold.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>
    /// The task as it now stands; <see cref="PersonalTaskRevisionResult.NotFound" /> when this person holds none under
    /// that identity, or when the edit moves its anchor to a thread or event they cannot open; or <see cref="PersonalTaskRevisionResult.AnsweredWait" /> when a reply answered it as a wait and
    /// the edit states the other direction.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="announcement" />, <paramref name="commitment" />, or <paramref name="opensAnchor" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="title" /> is blank, when one lead is stated twice, or when leads are stated against no due day.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="title" /> exceeds <see cref="PersonalTask.MaximumTitleLength" />, or when more than <see cref="Reminder.MaximumCount" /> leads are stated.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.TasksWrite" />.</exception>
    /// <remarks>
    /// The task is read before it is written because an edit is stated against the record it edits: the answer is what
    /// the person now holds rather than what they sent, and a task erased between the two is reported as one they do
    /// not hold rather than written back into existence.
    /// <para>
    /// What announces the task is part of the edit rather than beside it, which is what makes turning the last
    /// reminder off a task stated with none rather than a field left out — and what makes taking the date off one
    /// take its reminders with it, since a lead measured back from no due day is refused.
    /// </para>
    /// <para>
    /// The commitment is part of the edit for the same reason: a plan taken off a task, or an anchor removed, is stated
    /// as absent rather than left out. The one thing an edit cannot change is the direction of a wait a reply has
    /// answered, because the answer belongs to the wait it closed — and a reply arriving after the read and before the
    /// write is refused by the store in the same words.
    /// </para>
    /// <para>
    /// An anchor the task already holds is kept without asking whether the person can still open it, because its label
    /// is stored exactly so that a task outlives access to what it was about: an account taken away or an event deleted
    /// must not lock the person out of editing their own task. Only an anchor the edit moves somewhere new is a new
    /// reading, and a place they cannot open answers as a task they do not hold, so nothing reports that it exists.
    /// </para>
    /// </remarks>
    public async Task<PersonalTaskRevisionResult> ReviseAsync(
        PersonalTaskId task,
        string title,
        DateOnly? dueOn,
        TaskAnnouncement announcement,
        TaskCommitment commitment,
        Func<TaskAnchor, CancellationToken, Task<bool>> opensAnchor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commitment);
        ArgumentNullException.ThrowIfNull(opensAnchor);

        this.authorization.RequirePermission(MailFathomPermission.TasksWrite);

        var held = await this.store.FindAsync(this.authorization.RequireUser(), task, cancellationToken);

        if (held is null)
        {
            return PersonalTaskRevisionResult.NotFound;
        }

        if (held.Answer is not null && commitment.Direction != held.Direction)
        {
            return PersonalTaskRevisionResult.AnsweredWait;
        }

        if (commitment.Anchor is { } anchor
            && !NamesTheSamePlace(anchor, held.Anchor)
            && !await opensAnchor(anchor, cancellationToken))
        {
            return PersonalTaskRevisionResult.NotFound;
        }

        var revision = held.Revise(title, dueOn, announcement, commitment, this.clock.GetUtcNow());

        var outcome = await this.store.ReviseAsync(revision, cancellationToken);

        if (outcome is not PersonalTaskChangeOutcome.Applied)
        {
            return outcome is PersonalTaskChangeOutcome.AnsweredWait
                ? PersonalTaskRevisionResult.AnsweredWait
                : PersonalTaskRevisionResult.NotFound;
        }

        this.signals.Publish(ClientSignal.TasksChanged(revision.User));

        return PersonalTaskRevisionResult.Revised(revision);
    }

    /// <summary>Turns one of the signed-in person's proposals into a task they owe.</summary>
    /// <param name="task">The task to accept.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The task as it now stands, or <see langword="null" /> when this person holds none under that identity.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.TasksWrite" />.</exception>
    /// <remarks>
    /// A task already asserted is the state the caller asked for rather than an error, so accepting twice is answered
    /// as done. Dismissing a proposal is the erasure instead: what a person declines is not a task they owe, and
    /// keeping a declined row would leave a list whose every reader had to remember to exclude it.
    /// </remarks>
    public Task<PersonalTask?> AcceptAsync(PersonalTaskId task, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.TasksWrite);

        var user = this.authorization.RequireUser();

        return this.ReadBackAsync(user, task, this.store.AcceptAsync(user, task, cancellationToken), cancellationToken);
    }

    /// <summary>Puts one of the signed-in person's tasks into a stated completion state.</summary>
    /// <param name="task">The task to change.</param>
    /// <param name="isCompleted">The completion state it is to stand in.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The task as it now stands, or <see langword="null" /> when this person holds none under that identity.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.TasksWrite" />.</exception>
    public Task<PersonalTask?> SetCompletionAsync(
        PersonalTaskId task,
        bool isCompleted,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.TasksWrite);

        var user = this.authorization.RequireUser();

        return this.ReadBackAsync(
            user,
            task,
            this.store.SetCompletionAsync(user, task, isCompleted, cancellationToken),
            cancellationToken);
    }

    /// <summary>Erases one of the signed-in person's tasks.</summary>
    /// <param name="task">The task to erase.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns><see langword="true" /> when a task was erased, and <see langword="false" /> when this person held none.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.TasksWrite" />.</exception>
    /// <remarks>
    /// One act rather than two, whichever origin the task carries: deleting something a person owes and dismissing
    /// something mail suggested leave the same list behind, and a second name for the same erasure would be a route
    /// whose only difference was the word a screen puts on the button.
    /// </remarks>
    public async Task<bool> EraseAsync(PersonalTaskId task, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.TasksWrite);

        var user = this.authorization.RequireUser();

        if (!await this.store.EraseAsync(user, task, cancellationToken))
        {
            return false;
        }

        this.signals.Publish(ClientSignal.TasksChanged(user));

        return true;
    }

    /// <summary>Reports whether two anchors name the same thread or event, whatever label each is drawn with.</summary>
    private static bool NamesTheSamePlace(TaskAnchor anchor, TaskAnchor? held) =>
        held is not null
        && anchor.Kind == held.Kind
        && anchor.ThreadId == held.ThreadId
        && anchor.EventId == held.EventId;

    /// <summary>Reduces the reading's own narrowing to the fingerprint its cursors carry.</summary>
    /// <remarks>
    /// The user and every narrowing the query states, because a boundary is a position in one narrowed list and names
    /// nothing in another. The user comes off the credential, so a cursor presented by somebody it was not issued to is
    /// refused; the origin is the route, so one issued for the proposals names no boundary in the commitments; and a
    /// walk of one thread, event, person, or window does not continue into another's. The window keeps the offset it
    /// opens in, since that offset decides which due days it reaches.
    /// </remarks>
    private static string FingerprintOf(UserId user, PersonalTaskQuery query) => PageFilterFingerprint.Of(
        user.Value.ToString("N", CultureInfo.InvariantCulture),
        query.Origin?.ToString(),
        query.Thread?.Value.ToString("N", CultureInfo.InvariantCulture),
        query.Event?.Value.ToString("N", CultureInfo.InvariantCulture),
        query.Counterpart?.NormalizedAddress,
        query.From?.ToString("O", CultureInfo.InvariantCulture),
        query.Until?.ToString("O", CultureInfo.InvariantCulture));

    /// <summary>Writes the boundary one page ended on as the opaque string a caller presents to continue the walk.</summary>
    /// <remarks>
    /// A due day is written as midnight UTC, which is what lets the day ordering share the encoding every other keyset
    /// cursor here uses; an undated task is written as the absent position that encoding already carries, which is the
    /// block the order puts last.
    /// </remarks>
    private static string Encode(PersonalTask last, string fingerprint) => KeysetCursorPayload
        .At(PositionOf(last.DueOn), last.Id.Value, fingerprint)
        .Encode();

    /// <summary>Reads the boundary a caller presented, refusing one this reading did not issue.</summary>
    private static bool TryReadBoundary(string cursor, string fingerprint, out PersonalTaskCursor? boundary)
    {
        boundary = null;

        if (!KeysetCursorPayload.TryDecode(cursor, out var payload)
            || !string.Equals(payload.FilterFingerprint, fingerprint, StringComparison.Ordinal))
        {
            return false;
        }

        boundary = PersonalTaskCursor.After(
            payload.Position is { } position ? DateOnly.FromDateTime(position.UtcDateTime) : null,
            PersonalTaskId.Create(payload.Identity));

        return true;
    }

    /// <summary>Writes a due day as the instant the shared encoding carries, and an undated task as no position at all.</summary>
    private static DateTimeOffset? PositionOf(DateOnly? dueOn) =>
        dueOn is { } day ? new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null;

    /// <summary>Reads back what a state change left, so a caller redraws the row from what this deployment holds.</summary>
    /// <remarks>
    /// The read is what makes one answer serve both acts: accepting and completing each move one column, and a client
    /// redrawing a row needs the whole task rather than the column it asked about. A task erased between the write and
    /// the read is reported as one the person does not hold, which is the state they are in by then.
    /// </remarks>
    private async Task<PersonalTask?> ReadBackAsync(
        UserId user,
        PersonalTaskId task,
        Task<PersonalTaskChangeOutcome> change,
        CancellationToken cancellationToken)
    {
        if (await change is not PersonalTaskChangeOutcome.Applied)
        {
            return null;
        }

        this.signals.Publish(ClientSignal.TasksChanged(user));

        return await this.store.FindAsync(user, task, cancellationToken);
    }

    /// <summary>Reduces what a caller asked for to a page size this deployment serves.</summary>
    private static int Bounded(int? pageSize) => pageSize switch
    {
        null or < 1 => DefaultPageSize,
        > MaximumPageSize => MaximumPageSize,
        var asked => asked.Value,
    };
}
