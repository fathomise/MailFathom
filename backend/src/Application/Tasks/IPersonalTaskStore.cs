// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Tasks;

namespace MailFathom.Application.Tasks;

/// <summary>Keeps what a person owes, native to this deployment.</summary>
/// <remarks>
/// Every reader and every producer goes through this port rather than the table, which is what keeps a proposal one
/// row: a stage that composed its own insert would write a second task when the person accepted the first, and two
/// producers deciding that separately is how a list starts showing the same commitment twice.
/// </remarks>
public interface IPersonalTaskStore
{
    /// <summary>Keeps one task.</summary>
    /// <param name="task">The task to keep.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>An operation that completes once the row is committed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="task" /> is <see langword="null" />.</exception>
    Task AddAsync(PersonalTask task, CancellationToken cancellationToken);

    /// <summary>Reads one bounded page of a person's tasks, soonest due first.</summary>
    /// <param name="user">The person whose list is read.</param>
    /// <param name="query">What the list is narrowed to, which <see cref="PersonalTaskQuery.EnsureAnswerable" /> accepts.</param>
    /// <param name="after">The position a continued walk reads beyond, or <see langword="null" /> for the first page.</param>
    /// <param name="limit">The greatest number of tasks the answer may hold.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The tasks, soonest due first, with the undated ones last, holding at most <paramref name="limit" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is not positive.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="query" /> is not answerable, or when <paramref name="after" /> is the struct default, which names no position.</exception>
    /// <remarks>
    /// Naming an origin is what reads the proposals apart from the commitments, so a screen can offer what mail
    /// suggested without it standing on the list as something the person agreed to. Completed tasks are read with the
    /// outstanding ones, because whether a list shows what is done is the screen's decision and not the store's.
    /// The boundary is a position rather than an offset, so a task dated, completed, or accepted while somebody is
    /// paging neither shifts the window nor causes a row to be repeated or skipped. Whether the cursor was issued to
    /// this user is the caller's question rather than the store's: this reads the user it is given and nothing else.
    /// </remarks>
    Task<IReadOnlyList<PersonalTask>> ReadAsync(
        UserId user,
        PersonalTaskQuery query,
        PersonalTaskCursor? after,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Reads every person's open waits that a reply in one thread would answer.</summary>
    /// <param name="thread">The thread a reply arrived in.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The awaited tasks, not yet completed, anchored to that thread or to one merged into it.</returns>
    /// <remarks>
    /// Read across users because a reply is settled for a mailbox rather than for a person, and whoever is waiting in
    /// that thread is who it answers. A task keeps the thread it was anchored to when that thread is later merged into
    /// another, so the read follows every merge that ends at <paramref name="thread" /> rather than matching the
    /// identifier alone — otherwise a reply arriving in the surviving thread would leave the wait open forever.
    /// </remarks>
    Task<IReadOnlyList<PersonalTask>> ReadAwaitingAnswerAsync(EmailThreadId thread, CancellationToken cancellationToken);

    /// <summary>Reads one of a person's tasks by the identity it was addressed under.</summary>
    /// <param name="user">The person whose task is read, which is what scopes the read.</param>
    /// <param name="task">The task to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The task, or <see langword="null" /> when this user holds none under that identity.</returns>
    /// <remarks>
    /// The user is part of the addressing rather than a filter applied after a lookup, exactly as it is on every write
    /// here, so a task another person holds is not found rather than found and refused. That is what keeps this from
    /// reporting whose tasks exist beside the caller's own.
    /// </remarks>
    Task<PersonalTask?> FindAsync(UserId user, PersonalTaskId task, CancellationToken cancellationToken);

    /// <summary>Writes what a person edited about one of their tasks.</summary>
    /// <param name="revision">The task as it is to stand, which is what <see cref="PersonalTask.Revise" /> produced.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns>What became of the request.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="revision" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// It writes the title, the day, and the commitment — direction, estimate, plan, anchor, and counterpart — and
    /// nothing else, and it addresses the row by the revision's own user and identity. The rest of the record moves
    /// through the acts that own it — accepting a proposal, completing a task, a reply answering a wait — so a revision
    /// that carried a stale origin, completion, or answer beside the edited text could not undo any of them.
    /// </remarks>
    Task<PersonalTaskChangeOutcome> ReviseAsync(PersonalTask revision, CancellationToken cancellationToken);

    /// <summary>Turns one of a person's proposals into a task they owe.</summary>
    /// <param name="user">The person whose task is accepted, which is what scopes the write.</param>
    /// <param name="task">The task to accept.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns>What became of the request.</returns>
    /// <remarks>
    /// It moves the row's origin rather than writing a second task, so a commitment keeps the identity it was derived
    /// under and whatever already points at it — a reminder, a citation — still points at it afterwards.
    /// </remarks>
    Task<PersonalTaskChangeOutcome> AcceptAsync(
        UserId user,
        PersonalTaskId task,
        CancellationToken cancellationToken);

    /// <summary>Puts one of a person's tasks into a stated completion state.</summary>
    /// <param name="user">The person whose task is changed, which is what scopes the write.</param>
    /// <param name="task">The task to change.</param>
    /// <param name="isCompleted">The completion state it is to stand in.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns>What became of the request.</returns>
    /// <remarks>
    /// The user is part of the addressing rather than a filter applied after a lookup, so a task another person holds
    /// is not found rather than found and refused — which is what keeps the answer identical for one that does not
    /// exist at all. Reopening a wait a reply answered clears that answer, since a reply closes only an open wait.
    /// </remarks>
    Task<PersonalTaskChangeOutcome> SetCompletionAsync(
        UserId user,
        PersonalTaskId task,
        bool isCompleted,
        CancellationToken cancellationToken);

    /// <summary>Closes one of a person's waits with the reply that answered it.</summary>
    /// <param name="user">The person whose task is answered, which is what scopes the write.</param>
    /// <param name="task">The task to answer.</param>
    /// <param name="answer">The reply that answered it.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns>
    /// <see cref="PersonalTaskChangeOutcome.Applied" /> when this call closed the wait, and
    /// <see cref="PersonalTaskChangeOutcome.NotFound" /> when this user holds no open wait under that identity.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="answer" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Whether the task is still an open wait is decided by the write itself, so a wait the person completed by hand,
    /// or one a second replica answered first, is left as it stands and reported as nothing to answer. That is what
    /// lets the caller tell the person only about the waits it actually closed.
    /// </remarks>
    Task<PersonalTaskChangeOutcome> AnswerAsync(
        UserId user,
        PersonalTaskId task,
        TaskAnswer answer,
        CancellationToken cancellationToken);

    /// <summary>Erases one of a person's tasks.</summary>
    /// <param name="user">The person whose task is erased, which is what scopes the write.</param>
    /// <param name="task">The task to erase.</param>
    /// <param name="cancellationToken">Cancels the erasure.</param>
    /// <returns><see langword="true" /> when a task was erased, and <see langword="false" /> when this user held none.</returns>
    /// <remarks>
    /// The row goes rather than being marked gone: a task nobody owes any more is not a record anything is owed about,
    /// and keeping one would leave a list whose every reader had to remember to exclude it. Erasing something already
    /// erased is the act the caller wanted rather than an error, so the answer says what happened and never what was
    /// asked for.
    /// </remarks>
    Task<bool> EraseAsync(UserId user, PersonalTaskId task, CancellationToken cancellationToken);
}
