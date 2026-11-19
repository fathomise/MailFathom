// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Tasks;

/// <summary>States what a task is as a commitment: whose move it waits on, how long it takes, when it is planned, and what and who it concerns.</summary>
/// <remarks>
/// Every part but the direction is optional, because a task is often written down before anybody knows how long it
/// takes or when it will be done. <see cref="PersonalTask" /> validates the parts, so a record cannot be built that the
/// task would then refuse.
/// </remarks>
/// <param name="Direction">Whether the person owes the task or waits for it.</param>
/// <param name="EstimatedMinutes">How long it is expected to take, or <see langword="null" /> where nobody has said.</param>
/// <param name="Plan">The time set aside for it, or <see langword="null" /> where none is.</param>
/// <param name="Anchor">The conversation or event it is about, or <see langword="null" /> where it is about neither.</param>
/// <param name="Counterpart">Who it is owed to or who owes it, or <see langword="null" /> where that is nobody in particular.</param>
public sealed record TaskCommitment(
    PersonalTaskDirection Direction,
    int? EstimatedMinutes,
    TaskPlan? Plan,
    TaskAnchor? Anchor,
    TaskCounterpart? Counterpart)
{
    /// <summary>Gets the commitment of a task the person owes and has said nothing more about, which is what a task was before it carried any of this.</summary>
    public static TaskCommitment Owed { get; } = new(PersonalTaskDirection.Owed, null, null, null, null);
}
