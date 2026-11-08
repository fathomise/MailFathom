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
/// An arbiter that refuses, does not answer, or answers without a rating, decides nothing: the lowest juror rating is kept
/// and the rating records <see cref="ArbiterDidNotRate" />. Otherwise a provider declining the arbiter's requests would
/// fail every rating it was asked about, and the run would measure that provider rather than the model under test.
/// </para>
/// <para>
/// A juror that refuses or does not answer fails nothing either: its verdict counts as unrated on every rating, so each
/// rating it leaves undecided escalates to the arbiter like any other doubt, and the outcome the rating records names
/// that a juror did not answer. When no juror answered at all, every rating escalates, and only a rating the arbiter does
/// not give either is recorded as <see cref="NotRated" />. Whatever went unanswered, every rating the panel names comes
/// back as a metric, carrying each silent judge's failure as an error diagnostic, so the scenario reports the shortfall
/// rather than the evaluation library recording an exception in place of the rating.
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

    /// <summary>No juror gave a rating, so there was nothing for the arbiter to settle, or no judge asked gave one.</summary>
    public const string NotRated = "not rated";

    /// <summary>Added to the outcome, in parentheses, where some juror refused or did not answer.</summary>
    public const string JurorDidNotAnswer = "a juror did not answer";

    /// <summary>Added to the outcome, in parentheses, where every juror refused or did not answer.</summary>
    public const string NoJurorAnswered = "no juror answered";

    /// <summary>What a judge that refused or did not answer is recorded as having rated.</summary>
    private const string DidNotAnswer = "did not answer";

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

        async Task<JudgeVerdict> AskOrRecordSilenceAsync(JudgeDeclaration judge)
        {
            try
            {
                return new JudgeVerdict(judge, await AskAsync(judge), Answered: true);
            }
            catch (Exception failure) when (failure is JudgeRefusedException or JudgeDidNotAnswerException)
            {
                return new JudgeVerdict(judge, new EvaluationResult(this.EvaluationMetricNames.Select(name => Unanswered(name, failure))), Answered: false);
            }
        }

        var jurorVerdicts = await Task.WhenAll(panel.Jurors.Select(AskOrRecordSilenceAsync));
        var answeringVerdicts = jurorVerdicts.Where(static verdict => verdict.Answered).ToArray();
        var unrated = this.EvaluationMetricNames
            .Where(name => answeringVerdicts.Length > 0 && answeringVerdicts.All(verdict => !IsRated(verdict.Result.Metrics[name])))
            .ToHashSet(StringComparer.Ordinal);
        var doubted = this.EvaluationMetricNames
            .Where(name => !unrated.Contains(name))
            .Where(name => !EveryJurorPasses([.. jurorVerdicts.Select(verdict => verdict.Result.Metrics[name])], thresholds))
            .ToHashSet(StringComparer.Ordinal);
        var arbiterVerdict = doubted.Count > 0 && panel.Arbiter is { } arbiter ? await AskOrRecordSilenceAsync(arbiter) : null;
        var silence = answeringVerdicts.Length switch
        {
            0 => NoJurorAnswered,
            var answering when answering < jurorVerdicts.Length => JurorDidNotAnswer,
            _ => null,
        };

        return new EvaluationResult(this.EvaluationMetricNames.Select(name => Decide(
            [.. jurorVerdicts.Select(verdict => verdict.RatingOf(name))],
            arbiterVerdict?.RatingOf(name),
            unrated.Contains(name),
            doubted.Contains(name),
            silence)));
    }

    private static NumericMetric Unanswered(string metricName, Exception failure)
    {
        var rating = new NumericMetric(metricName);
        rating.AddDiagnostics(EvaluationDiagnostic.Error(failure.Message));

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
        IReadOnlyList<JudgeRating> jurorRatings,
        JudgeRating? arbiterRating,
        bool unrated,
        bool doubted,
        string? silence)
    {
        var lowest = jurorRatings.MinBy(static asked => RankOf(asked.Rating))!;
        var noJurorRated = jurorRatings.All(static asked => !IsRated(asked.Rating));
        (string Outcome, JudgeRating Kept) decision = (unrated, doubted, arbiterRating) switch
        {
            (true, _, _) => (NotRated, lowest),
            (false, false, _) => (NotNeeded, lowest),
            (false, true, { } arbiter) when IsRated(arbiter.Rating) => (Escalated, arbiter),
            _ when noJurorRated => (NotRated, lowest),
            (false, true, { }) => (ArbiterDidNotRate, lowest),
            (false, true, null) => (NeededWithoutArbiter, lowest),
        };

        IEnumerable<JudgeRating> asked = arbiterRating is { } answered ? [.. jurorRatings, answered] : jurorRatings;
        var kept = decision.Kept.Rating;

        kept.AddOrUpdateMetadata(EscalationMetadata, silence is null ? decision.Outcome : $"{decision.Outcome} ({silence})");
        kept.AddOrUpdateMetadata(DecidedByMetadata, decision.Kept.Judge.Role);

        foreach (var (judge, rating, judgeAnswered) in asked)
        {
            kept.AddOrUpdateMetadata(RatingMetadataOf(judge), judgeAnswered ? ValueOf(rating) : DidNotAnswer);

            if (judgeAnswered)
            {
                kept.AddDiagnostics(EvaluationDiagnostic.Informational($"{judge.Role} rated {ValueOf(rating)}: {rating.Reason}"));
            }

            if (!ReferenceEquals(rating, kept))
            {
                kept.AddDiagnostics((rating.Diagnostics ?? []).Select(diagnostic => new EvaluationDiagnostic(diagnostic.Severity, $"{judge.Role}: {diagnostic.Message}")));
            }
        }

        return kept;
    }

    /// <summary>What one judge of the panel came back with, or the unrated verdict standing in for one that did not answer.</summary>
    private sealed record JudgeVerdict(JudgeDeclaration Judge, EvaluationResult Result, bool Answered)
    {
        public JudgeRating RatingOf(string metricName) => new(this.Judge, this.Result.Metrics[metricName], this.Answered);
    }

    /// <summary>One judge's rating of one metric, and whether that judge answered at all.</summary>
    private sealed record JudgeRating(JudgeDeclaration Judge, EvaluationMetric Rating, bool Answered);
}
