// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace MailFathom.Evaluations.Judging;

/// <summary>Asks every juror of the panel for one evaluator's ratings, and the arbiter only for a rating they do not all pass.</summary>
/// <remarks>
/// <para>
/// A numeric rating passes at or above the threshold <see cref="JudgeThresholds" /> states for it, and a boolean one when
/// it is true. A rating a juror did not give passes nothing. A rating whose scenario states no threshold is treated as
/// passing when every juror gives the same value, because without a threshold the only doubt left is disagreement.
/// </para>
/// <para>
/// A rating no juror gave is not escalated. Every juror comes back unrated when the evaluator refused the answer itself —
/// an empty response above all, which it refuses before asking any judge — so the arbiter would refuse it the same way
/// and there is nothing for it to settle. The verdict is then recorded as <see cref="NotRated" />, and the scenario still
/// fails the rating it did not get.
/// </para>
/// <para>
/// Where every juror passes, the verdict is the lowest of their ratings and the arbiter is not asked. Where one does not
/// and an arbiter is declared, the arbiter's rating is the verdict, never a vote beside the jurors' — escalating exists
/// so the stronger judge settles what the cheaper ones left in doubt. Where no arbiter is declared, the verdict is the
/// lowest juror rating, so a juror-only run is never kinder than the jurors were.
/// </para>
/// <para>
/// An arbiter that refuses, or answers without a rating, decides nothing: the lowest juror rating is kept and the rating
/// records <see cref="ArbiterDidNotRate" />. Otherwise a provider declining the arbiter's requests would fail every
/// rating it was asked about, and the run would measure that provider rather than the model under test.
/// </para>
/// <para>
/// The rating kept records what every judge asked answered: <c>judge-juror-1</c> and its siblings, <c>judge-arbiter</c>
/// where it was asked, <see cref="DecidedByMetadata" />, and <see cref="EscalationMetadata" />, with each judge's reason as
/// an informational diagnostic. Judges are named by role and never by model.
/// </para>
/// </remarks>
/// <param name="inner">The evaluator every judge is asked through.</param>
/// <param name="panel">The panel whose judges are asked.</param>
internal sealed class JudgePanelEvaluator(IEvaluator inner, JudgePanel panel) : IEvaluator
{
    /// <summary>The metadata naming whether a rating was escalated to the arbiter.</summary>
    public const string EscalationMetadata = "judge-escalation";

    /// <summary>The metadata naming the role of the judge whose rating was kept.</summary>
    public const string DecidedByMetadata = "judge-decided-by";

    /// <summary>Every juror passed, so the arbiter was not asked.</summary>
    public const string NotNeeded = "not needed";

    /// <summary>A juror did not pass, and the arbiter's rating was kept.</summary>
    public const string Escalated = "escalated";

    /// <summary>A juror did not pass and no arbiter is declared, so the lowest juror rating was kept.</summary>
    public const string NeededWithoutArbiter = "needed, no arbiter declared";

    /// <summary>A juror did not pass and the arbiter gave no rating, so the lowest juror rating was kept.</summary>
    public const string ArbiterDidNotRate = "escalated, arbiter did not rate";

    /// <summary>No juror gave a rating, so there was nothing for the arbiter to settle.</summary>
    public const string NotRated = "not rated";

    /// <inheritdoc />
    public IReadOnlyCollection<string> EvaluationMetricNames => inner.EvaluationMetricNames;

    /// <summary>Gets the name of the metadata recording what one judge rated.</summary>
    /// <param name="judge">The judge.</param>
    /// <returns>The metadata's name, such as <c>judge-juror-1</c>.</returns>
    public static string RatingMetadataOf(JudgeDeclaration judge) => $"judge-{judge.Role.Replace(' ', '-')}";

    /// <inheritdoc />
    public async ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chatConfiguration);

        ChatMessage[] conversation = [.. messages];
        EvaluationContext[] context = [.. additionalContext ?? []];
        var thresholds = context.OfType<JudgeThresholds>().FirstOrDefault();

        Task<EvaluationResult> AskAsync(JudgeDeclaration judge) =>
            inner.EvaluateAsync(
                conversation,
                modelResponse,
                new ChatConfiguration(JudgePanelChatClient.Asking(chatConfiguration.ChatClient, judge)),
                context,
                cancellationToken).AsTask();

        var jurorVerdicts = await Task.WhenAll(panel.Jurors.Select(AskAsync));
        var unrated = this.EvaluationMetricNames
            .Where(name => jurorVerdicts.All(verdict => !IsRated(verdict.Metrics[name])))
            .ToHashSet(StringComparer.Ordinal);
        var doubted = this.EvaluationMetricNames
            .Where(name => !unrated.Contains(name))
            .Where(name => !EveryJurorPasses([.. jurorVerdicts.Select(verdict => verdict.Metrics[name])], thresholds))
            .ToHashSet(StringComparer.Ordinal);
        var arbiterVerdict = doubted.Count > 0 && panel.Arbiter is { } arbiter ? await AskArbiterAsync(arbiter) : null;

        async Task<EvaluationResult> AskArbiterAsync(JudgeDeclaration arbiter)
        {
            try
            {
                return await AskAsync(arbiter);
            }
            catch (JudgeRefusedException refusal)
            {
                return new EvaluationResult(this.EvaluationMetricNames.Select(name => Refused(name, refusal)));
            }
        }

        return new EvaluationResult(this.EvaluationMetricNames.Select(name => Decide(
            [.. panel.Jurors.Zip(jurorVerdicts, (juror, verdict) => (juror, verdict.Metrics[name]))],
            arbiterVerdict is null ? null : (panel.Arbiter!, arbiterVerdict.Metrics[name]),
            unrated.Contains(name),
            doubted.Contains(name))));
    }

    private static NumericMetric Refused(string metricName, JudgeRefusedException refusal)
    {
        var rating = new NumericMetric(metricName);
        rating.AddDiagnostics(EvaluationDiagnostic.Error(refusal.Message));

        return rating;
    }

    private static bool IsRated(EvaluationMetric rating) =>
        rating is not (NumericMetric { Value: null } or BooleanMetric { Value: null });

    private static bool EveryJurorPasses(IReadOnlyList<EvaluationMetric> ratings, JudgeThresholds? thresholds)
    {
        var verdicts = ratings.Select(rating => Passes(rating, thresholds)).ToArray();

        return verdicts.All(static passes => passes is not null)
            ? verdicts.All(static passes => passes is true)
            : ratings.Select(ValueOf).Distinct(StringComparer.Ordinal).Count() is 1;
    }

    private static bool? Passes(EvaluationMetric rating, JudgeThresholds? thresholds) => rating switch
    {
        NumericMetric { Value: null } => false,
        NumericMetric numeric => thresholds?.MinimumFor(numeric.Name) is { } minimum ? numeric.Value >= minimum : null,
        BooleanMetric boolean => boolean.Value is true,
        _ => null,
    };

    /// <summary>Orders ratings from the lowest: an unrated one first, then by value.</summary>
    private static double RankOf(EvaluationMetric rating) => rating switch
    {
        NumericMetric { Value: { } value } => value,
        BooleanMetric { Value: { } value } => value ? 1 : 0,
        _ => double.NegativeInfinity,
    };

    private static string ValueOf(EvaluationMetric rating) => rating switch
    {
        NumericMetric { Value: { } value } => value.ToString("0.##", CultureInfo.InvariantCulture),
        BooleanMetric { Value: { } value } => value ? "true" : "false",
        NumericMetric or BooleanMetric => "not rated",
        _ => rating.Reason ?? "not rated",
    };

    private static EvaluationMetric Decide(
        IReadOnlyList<(JudgeDeclaration Judge, EvaluationMetric Rating)> jurorRatings,
        (JudgeDeclaration Judge, EvaluationMetric Rating)? arbiterRating,
        bool unrated,
        bool doubted)
    {
        var lowest = jurorRatings.MinBy(static asked => RankOf(asked.Rating));
        (string Outcome, (JudgeDeclaration Judge, EvaluationMetric Rating) Kept) decision = (unrated, doubted, arbiterRating) switch
        {
            (true, _, _) => (NotRated, lowest),
            (false, false, _) => (NotNeeded, lowest),
            (false, true, { } arbiter) when IsRated(arbiter.Rating) => (Escalated, arbiter),
            (false, true, { }) => (ArbiterDidNotRate, lowest),
            (false, true, null) => (NeededWithoutArbiter, lowest),
        };

        IEnumerable<(JudgeDeclaration Judge, EvaluationMetric Rating)> asked =
            arbiterRating is { } answered ? [.. jurorRatings, answered] : jurorRatings;
        var kept = decision.Kept.Rating;

        kept.AddOrUpdateMetadata(EscalationMetadata, decision.Outcome);
        kept.AddOrUpdateMetadata(DecidedByMetadata, decision.Kept.Judge.Role);

        foreach (var (judge, rating) in asked)
        {
            kept.AddOrUpdateMetadata(RatingMetadataOf(judge), ValueOf(rating));
            kept.AddDiagnostics(EvaluationDiagnostic.Informational($"{judge.Role} rated {ValueOf(rating)}: {rating.Reason}"));

            if (!ReferenceEquals(rating, kept))
            {
                kept.AddDiagnostics((rating.Diagnostics ?? []).Select(diagnostic => new EvaluationDiagnostic(diagnostic.Severity, $"{judge.Role}: {diagnostic.Message}")));
            }
        }

        return kept;
    }
}
