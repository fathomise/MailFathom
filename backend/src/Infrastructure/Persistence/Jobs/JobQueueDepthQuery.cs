// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Jobs;

/// <summary>Composes the query an enqueue asks whether one type's queue is already as deep as the deployment accepts.</summary>
/// <remarks>
/// <para>
/// A type of its own for the reason the enqueue and claim statements are: the question is whether the depth has been
/// reached, and both halves of that are easy to get wrong in a way only the database would notice. Counting a state that
/// is not waiting would make a draining instance look like a filling one, and counting the whole queue rather than
/// stopping at the bound would make every enqueue pay for the size of a backlog it only needs to compare against.
/// </para>
/// <para>
/// The bound is applied as a ceiling on the rows read rather than as a predicate, so the answer is <em>at least this
/// many</em> rather than <em>how many</em>. That is the whole of what the caller compares, and it keeps the cost the
/// same on a queue of a thousand and on a queue of a million.
/// </para>
/// <para>
/// The rows read are the first the claim would take, in the claim's own order: <c>TurnAt</c>, which the partial claim
/// index holds within a job type, then <c>Id</c>, which the index does not hold and which only breaks a tie between two
/// turns. The count would come out the same in any order, but a limit nothing orders leaves which rows were read to the
/// plan, and EF Core reports exactly that on every deployment that compiles the query.
/// </para>
/// <para>
/// A job of a mail account nobody is assigned is not counted, because the claim passes it over and it waits for the
/// account to be assigned again rather than for a worker. Counted, a backlog left behind by an unassigned mailbox would
/// hold the bound for every other mailbox's enqueue of that type for as long as nobody was assigned it.
/// </para>
/// </remarks>
internal static class JobQueueDepthQuery
{
    /// <summary>Composes the query over the jobs of one type that are waiting, reading no more than the bound.</summary>
    /// <param name="jobs">The job rows to ask, ordinarily read without tracking.</param>
    /// <param name="assignments">The mail account assignments, which decide whether a job naming an account is waiting for a worker.</param>
    /// <param name="jobTypeName">The name of the type whose queue is being measured.</param>
    /// <param name="depthBound">The depth to compare against, which is also the greatest number of rows read.</param>
    /// <returns>The query whose count reaches the bound exactly when the queue is full.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="jobs" /> or <paramref name="assignments" /> is <see langword="null" />.
    /// </exception>
    internal static IQueryable<JobEntity> Compose(
        IQueryable<JobEntity> jobs,
        IQueryable<MailAccountAssignmentEntity> assignments,
        string jobTypeName,
        int depthBound)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(assignments);

        return jobs
            .Where(job => job.JobType == jobTypeName && job.State == JobState.Pending)
            .Where(job => job.MailboxAccountId == null
                || assignments.Any(assignment => assignment.MailAccountId.ToString() == job.MailboxAccountId))
            .OrderBy(job => job.TurnAt)
            .ThenBy(job => job.Id)
            .Take(depthBound);
    }
}
