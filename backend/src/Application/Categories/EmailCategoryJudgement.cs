// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Decisions;

namespace MailFathom.Application.Categories;

/// <summary>What one consultation of the model came back with: the categories a message holds, or the reason there are none.</summary>
public sealed record EmailCategoryJudgement
{
    private EmailCategoryJudgement(DecisionModelOutcome outcome, IReadOnlyList<EmailCategoryAssignment> assignments)
    {
        this.Outcome = outcome;
        this.Assignments = assignments;
    }

    /// <summary>Gets how the consultation ended.</summary>
    public DecisionModelOutcome Outcome { get; }

    /// <summary>Gets exactly one default category and every own category over the threshold, empty unless the model answered.</summary>
    public IReadOnlyList<EmailCategoryAssignment> Assignments { get; }

    /// <summary>Records an answer the model gave.</summary>
    /// <param name="assignments">What the message holds.</param>
    /// <returns>The judgement.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assignments" /> is <see langword="null" />.</exception>
    public static EmailCategoryJudgement Answered(IReadOnlyList<EmailCategoryAssignment> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);

        return new EmailCategoryJudgement(DecisionModelOutcome.Answered, assignments);
    }

    /// <summary>Records that the model gave no answer, and why.</summary>
    /// <param name="outcome">The reason.</param>
    /// <returns>The judgement.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="outcome" /> is <see cref="DecisionModelOutcome.Answered" /> or not a defined member.</exception>
    public static EmailCategoryJudgement Unanswered(DecisionModelOutcome outcome) =>
        outcome is DecisionModelOutcome.Answered || !Enum.IsDefined(outcome)
            ? throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "A judgement without an answer names why there is none.")
            : new EmailCategoryJudgement(outcome, []);
}
