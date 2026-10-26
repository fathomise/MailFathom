// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using MailFathom.AI.SpamClassification;
using MailFathom.Application.Spam.Model;
using MailFathom.Domain.Spam;
using Microsoft.Extensions.AI.Evaluation;

namespace MailFathom.Evaluations.SpamClassification;

/// <summary>What the stage recorded for every labelled message under one model.</summary>
/// <remarks>
/// Handed to <see cref="SpamClassificationEvaluator" /> as the context it reads, for the reason
/// <see cref="RelevanceFilter.RelevanceFilterMeasurement" /> gives: what is measured is a count over many messages the
/// scenario classified, which is neither a conversation nor an answer. The store keeps it beside the result, one line
/// per message with the spam probability the model gave, so the report is also the evidence a threshold is moved from.
/// </remarks>
internal sealed class SpamClassificationMeasurement : EvaluationContext
{
    /// <summary>The name the context is filed under.</summary>
    public const string ContextName = "Labelled messages classified";

    /// <summary>Initializes the measurement.</summary>
    /// <param name="outcomes">What the stage recorded for each message, in the order the cases are listed.</param>
    public SpamClassificationMeasurement(IReadOnlyList<SpamCaseOutcome> outcomes)
        : this(outcomes, string.Join('\n', outcomes.Select(static outcome => outcome.Describe())))
    {
    }

    private SpamClassificationMeasurement(IReadOnlyList<SpamCaseOutcome> outcomes, string summary)
        : base(ContextName, summary)
    {
        this.Outcomes = outcomes;
        this.Summary = summary;
    }

    /// <summary>Gets what the stage recorded for each message.</summary>
    public IReadOnlyList<SpamCaseOutcome> Outcomes { get; }

    /// <summary>Gets one line per message, as the report shows it.</summary>
    public string Summary { get; }
}

/// <summary>What the stage recorded for one labelled message, and how its consultation of the model ended.</summary>
/// <param name="Case">The message and its label.</param>
/// <param name="AskedJob">The job the stage asked the model to do, or <see langword="null" /> where it asked nothing.</param>
/// <param name="Consultation">How the consultation ended, or <see langword="null" /> where the stage asked nothing.</param>
/// <param name="Verdict">The verdict recorded.</param>
/// <param name="Subcategory">The kind recorded beside a spam verdict.</param>
/// <param name="SpamProbability">The probability the model gave that the message is spam, where the model decided the verdict.</param>
internal sealed record SpamCaseOutcome(
    SpamClassificationCase Case,
    SpamModelJob? AskedJob,
    SpamModelOutcome? Consultation,
    SpamVerdict Verdict,
    SpamSubcategory? Subcategory,
    double? SpamProbability)
{
    /// <summary>Gets whether the message was recorded as spam.</summary>
    public bool IsCalledSpam => this.Verdict is SpamVerdict.Spam;

    /// <summary>Gets whether the stage asked the model the job the case's headers leave it and the model answered.</summary>
    public bool IsAnswered => this.Consultation is SpamModelOutcome.Answered && this.AskedJob == this.Case.Job;

    /// <summary>Gets what was recorded, as the option the question names it by, which is what a hostile message dictates.</summary>
    public string Recorded => this.IsCalledSpam
        ? this.Subcategory is { } kind ? OptionOf(kind) : "spam of no kind"
        : SpamClassificationQuestions.NotSpamOption;

    /// <summary>Gets what the message is, as the option the question names it by.</summary>
    public string Labelled => this.Case.Expected is { } kind ? OptionOf(kind) : SpamClassificationQuestions.NotSpamOption;

    /// <summary>Names the option the questions offer for one kind.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The option's value.</returns>
    public static string OptionOf(SpamSubcategory kind) =>
        SpamClassificationQuestions.KindOptions.Single(option => SpamClassificationQuestions.KindOf(option) == kind);

    /// <summary>Describes the outcome in one line.</summary>
    /// <returns>The case, its label, what was recorded, and the probability or the consultation's end where there is no probability.</returns>
    public string Describe()
    {
        var detail = this.SpamProbability is { } probability
            ? string.Create(CultureInfo.InvariantCulture, $"at a spam probability of {probability:0.000}")
            : this.IsAnswered ? "with the verdict the deterministic stage reached" : $"with the model's consultation ending {this.Consultation?.ToString() ?? "unasked"}";

        return $"{this.Case.Name}: labelled {this.Labelled}, recorded {this.Recorded} {detail}.";
    }
}
