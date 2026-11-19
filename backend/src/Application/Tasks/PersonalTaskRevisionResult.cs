// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Tasks;

namespace MailFathom.Application.Tasks;

/// <summary>What became of a person's edit to one of their tasks, and the task as it stands where the edit was written.</summary>
/// <remarks>
/// The task travels with the outcome because the answer to an edit is what the person now holds rather than what they
/// sent. A refusal carries none, which keeps a refused edit from reporting anything about the task it did not change.
/// </remarks>
public sealed record PersonalTaskRevisionResult
{
    private PersonalTaskRevisionResult(PersonalTaskChangeOutcome outcome, PersonalTask? task)
    {
        this.Outcome = outcome;
        this.Task = task;
    }

    /// <summary>Gets what became of the edit.</summary>
    public PersonalTaskChangeOutcome Outcome { get; }

    /// <summary>Gets the task as the list now holds it, or <see langword="null" /> where nothing was written.</summary>
    public PersonalTask? Task { get; }

    /// <summary>Gets the result of an edit naming a task this person does not hold.</summary>
    public static PersonalTaskRevisionResult NotFound { get; } = new(PersonalTaskChangeOutcome.NotFound, null);

    /// <summary>Gets the result of an edit that would change the direction of a wait a reply has answered.</summary>
    public static PersonalTaskRevisionResult AnsweredWait { get; } = new(PersonalTaskChangeOutcome.AnsweredWait, null);

    /// <summary>States that the list holds the task as revised.</summary>
    /// <param name="task">The task as it now stands.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="task" /> is <see langword="null" />.</exception>
    public static PersonalTaskRevisionResult Revised(PersonalTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        return new PersonalTaskRevisionResult(PersonalTaskChangeOutcome.Applied, task);
    }
}
