// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Categories;
using MailFathom.Application.Decisions;
using MailFathom.Domain.Categories;
using Microsoft.Extensions.AI.Evaluation;

namespace MailFathom.Evaluations.EmailCategorization;

/// <summary>What categorization recorded for every labelled message, and what a request's questions were counted at.</summary>
internal sealed class EmailCategorizationMeasurement : EvaluationContext
{
    /// <summary>The name the context is filed under.</summary>
    public const string ContextName = "Labelled messages categorized";

    /// <summary>Initializes the measurement.</summary>
    /// <param name="outcomes">What was recorded for each message.</param>
    /// <param name="inputCounts">The input tokens the provider counted for one message under each number of own categories.</param>
    public EmailCategorizationMeasurement(
        IReadOnlyList<EmailCategoryCaseOutcome> outcomes,
        IReadOnlyList<(int OwnCategories, long? InputTokens)> inputCounts)
        : this(outcomes, inputCounts, string.Join('\n', outcomes.Select(static outcome => outcome.Describe())))
    {
    }

    private EmailCategorizationMeasurement(
        IReadOnlyList<EmailCategoryCaseOutcome> outcomes,
        IReadOnlyList<(int OwnCategories, long? InputTokens)> inputCounts,
        string summary)
        : base(ContextName, summary)
    {
        this.Outcomes = outcomes;
        this.InputCounts = inputCounts;
        this.Summary = summary;
    }

    /// <summary>Gets what was recorded for each message.</summary>
    public IReadOnlyList<EmailCategoryCaseOutcome> Outcomes { get; }

    /// <summary>Gets the input tokens the provider counted under each number of own categories.</summary>
    public IReadOnlyList<(int OwnCategories, long? InputTokens)> InputCounts { get; }

    /// <summary>Gets one line per message, as the report shows it.</summary>
    public string Summary { get; }
}

/// <summary>What categorization recorded for one labelled message.</summary>
/// <param name="Case">The message.</param>
/// <param name="Consultation">How the consultation ended.</param>
/// <param name="Assignments">What the message was said to hold.</param>
internal sealed record EmailCategoryCaseOutcome(
    EmailCategorizationCase Case,
    DecisionModelOutcome Consultation,
    IReadOnlyList<EmailCategoryAssignment> Assignments)
{
    /// <summary>Gets whether the model answered.</summary>
    public bool IsAnswered => this.Consultation is DecisionModelOutcome.Answered;

    /// <summary>Gets the default category recorded, or <see langword="null" /> where none was.</summary>
    public string? Default => this.Assignments.FirstOrDefault(static assignment => assignment.Kind == EmailCategoryKind.Default)?.Category;

    /// <summary>Gets the own categories recorded.</summary>
    public IEnumerable<string> Own => this.Assignments
        .Where(static assignment => assignment.Kind == EmailCategoryKind.Own)
        .Select(static assignment => assignment.Category);

    /// <summary>Describes the outcome in one line.</summary>
    /// <returns>The case, its label, and what was recorded.</returns>
    public string Describe() =>
        $"{this.Case.Name}: labelled {this.Case.Expected} [{string.Join(", ", this.Case.Matches.Select(static own => own.Name))}], recorded {this.Default ?? this.Consultation.ToString()} [{string.Join(", ", this.Own)}].";
}
