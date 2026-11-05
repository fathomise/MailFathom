// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.Rules.Evaluation;

namespace MailFathom.Host.Configuration.Rules;

/// <summary>Declares the limits every mail account's rules are read and evaluated under.</summary>
/// <remarks>
/// <para>
/// The rules themselves are not configuration: each mail account's rules are kept in PostgreSQL and changed while the
/// deployment runs, which is what
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0048-keeping-a-mail-accounts-rules-in-postgresql-and-changing-them-at-runtime.md">ADR 0048</see>
/// decides. What stays here is what an operator bounds for every account alike.
/// </para>
/// <para>
/// The whole section is optional and every limit has a usable default.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class MailRulesOptions : IValidatableObject
{
    /// <summary>The configuration section this declaration is bound from.</summary>
    public const string SectionName = "MailRules";

    /// <summary>Gets or sets the rules an earlier release read from this section, which nothing applies any more.</summary>
    /// <remarks>
    /// Recognized rather than bound, so the strict binder does not read the retired key as a misspelling and stop a
    /// deployment that still carries it. <c>ConfiguredMailRulesIgnoredWarning</c> reports how many it declares.
    /// </remarks>
    public IConfigurationSection? Rules { get; set; }

    /// <summary>Gets or sets the greatest number of characters one condition may be written in.</summary>
    [Range(1, 10_000)]
    public int MaxConditionLength { get; set; } = 1_000;

    /// <summary>Gets or sets the greatest depth one condition may nest to.</summary>
    [Range(1, 64)]
    public int MaxConditionNestingDepth { get; set; } = 16;

    /// <summary>The longest a condition may be given, which is the ceiling the two written limits have and this one needs.</summary>
    /// <remarks>
    /// A timeout is the only bound on a condition that nothing about the text can supply, so an operator who meant
    /// milliseconds and wrote minutes would otherwise let one condition hold a pass for as long as it liked, per email.
    /// Thirty seconds is far above anything a metadata comparison or a single stored-content read needs and far below
    /// the point at which a stuck condition stops being visible as one.
    /// </remarks>
    public static readonly TimeSpan LongestConditionEvaluationTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets how long one condition may take to evaluate, including resolving the facts it names.</summary>
    public TimeSpan ConditionEvaluationTimeout { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets or sets how many stored emails one evaluation batch reads, evaluates, and commits together.</summary>
    /// <remarks>
    /// A batch is the unit of progress an interrupted pass gives back, and the unit of work one transaction covers.
    /// Nothing about it is a schedule: when a pass happens is the account's synchronization interval.
    /// </remarks>
    [Range(1, 10_000)]
    public int EvaluationBatchSize { get; set; } = 200;

    /// <summary>Gets or sets how many batches one walk of one pass may commit before leaving the rest to the next run.</summary>
    /// <remarks>
    /// What bounds a run rather than what bounds the work: an account whose mail has never been evaluated drains over
    /// as many runs as its size needs, instead of turning one run into a walk of its whole history while the folders
    /// that run exists to fetch wait behind it.
    /// </remarks>
    [Range(1, 1_000)]
    public int MaxEvaluationBatchesPerPass { get; set; } = 5;

    /// <summary>Turns the declared limits into the bounds a condition is read and run under.</summary>
    /// <returns>The bounds.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a limit is not positive, which validation refuses first.</exception>
    public MailRuleConditionBounds ToBounds() => MailRuleConditionBounds.Create(
        this.MaxConditionLength,
        this.MaxConditionNestingDepth,
        this.ConditionEvaluationTimeout);

    /// <summary>Gets or sets how long a recorded rule execution is kept before the account's next run erases it.</summary>
    /// <remarks>
    /// The history's one bound of its own. A pass records what every rule it reached concluded about every message it
    /// evaluated, which is what makes "this rule has never matched" and "this rule was never asked" different answers —
    /// and also why a deployment left alone accumulates a row per rule per message. Everything else about the record's
    /// lifetime it inherits from the mail it describes: an erased message takes the executions naming it with it,
    /// whatever this says. Zero or less declares no window at all, which keeps every execution until its message goes.
    /// </remarks>
    public TimeSpan HistoryRetention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>The longest history retention accepted, beyond which the window stops being one anybody could justify holding.</summary>
    /// <remarks>
    /// Ten years, which is the ceiling both audit trails are bound by and for the same reason: a retention nobody can
    /// justify is what a storage-limitation setting exists to prevent. There is no floor to match theirs, because zero
    /// here means something rather than naming a window too short to read — it declares that an execution is kept for
    /// exactly as long as the message it names.
    /// </remarks>
    public static readonly TimeSpan LongestHistoryRetention = TimeSpan.FromDays(3650);

    /// <summary>Turns the declared limits into the bounds one evaluation pass runs under.</summary>
    /// <returns>The bounds.</returns>
    public MailRuleEvaluationOptions ToEvaluationOptions() => new()
    {
        BatchSize = this.EvaluationBatchSize,
        MaxBatchesPerPass = this.MaxEvaluationBatchesPerPass,
        HistoryRetention = this.HistoryRetention,
    };

    /// <summary>Finds every refusal the limits carry, the attribute bounds and <see cref="Validate" /> together.</summary>
    /// <returns>One sentence per refused limit, empty when every limit is usable.</returns>
    public IReadOnlyList<string> FindConfigurationErrors()
    {
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(this, new ValidationContext(this), results, validateAllProperties: true);

        return
        [
            .. results
                .Where(result => !string.IsNullOrWhiteSpace(result.ErrorMessage))
                .Select(result => $"{SectionName}:{result.MemberNames.FirstOrDefault()} — {result.ErrorMessage}"),
        ];
    }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (this.ConditionEvaluationTimeout <= TimeSpan.Zero)
        {
            yield return new ValidationResult(
                $"{SectionName} declares a ConditionEvaluationTimeout that is not positive, so every condition would run out of time before it started and every rule would be recorded as failed.",
                [nameof(this.ConditionEvaluationTimeout)]);
        }
        else if (this.ConditionEvaluationTimeout > LongestConditionEvaluationTimeout)
        {
            yield return new ValidationResult(
                $"{SectionName} declares a ConditionEvaluationTimeout of {this.ConditionEvaluationTimeout}, and a condition may be given at most {LongestConditionEvaluationTimeout}. A timeout above that stops bounding what one rule costs per email.",
                [nameof(this.ConditionEvaluationTimeout)]);
        }

        if (this.HistoryRetention > LongestHistoryRetention)
        {
            yield return new ValidationResult(
                $"{SectionName} declares a HistoryRetention of {this.HistoryRetention}, and a rule execution may be kept for at most {LongestHistoryRetention}. Set it to zero to keep the history for exactly as long as the mail it describes.",
                [nameof(this.HistoryRetention)]);
        }
    }
}
