// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.Spam.Signals;
using MailFathom.Domain.Spam;

namespace MailFathom.Application.Spam.Model;

/// <summary>Decides what one classification records once the deterministic stage has read the message, asking the model where the account consults it.</summary>
/// <remarks>
/// <para>
/// The model does one of two jobs, and the deterministic verdict decides which. Where it reached no spam verdict, the
/// model decides the verdict and, on spam, its subcategory: a message the mail server let through is where a missed
/// phishing message costs most. Where it reached spam, that verdict stands and no answer can change it; the model only
/// names the subcategory, and only where the account switched that half on, since junk is often most of a mailbox.
/// </para>
/// <para>
/// The model is never asked about a message carrying <c>$NotJunk</c> somebody other than this deployment set, because
/// that keyword is the person's own correction, and the person outranks every stage. The person's recorded verdict is
/// never reached here at all: the classifier returns it before content is read.
/// </para>
/// <para>
/// <b>Whole or absent.</b> An answer outside the closed set, an unreachable provider, a call past its budget, input past
/// its bound, and no model declared each leave the deterministic result exactly as it would have been, with no model
/// stage and no model signal in the record. Such a record is written under the deterministic profile rather than the
/// account's, so the terms it names are the ones it was actually decided under, and a classification run that comes
/// later asks the model again.
/// </para>
/// <para>
/// Everything that leaves is guarded under the posture of the account the message belongs to, which this enters before
/// the model is asked, so a scanner that account switched on reads the input whichever flow asked for the classification.
/// </para>
/// </remarks>
public sealed class SpamModelStage
{
    private readonly ISpamModelJudge judge;
    private readonly SpamModelEvidenceReader evidenceReader;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly ISpamModelStageTelemetry telemetry;

    /// <summary>Initializes the stage.</summary>
    /// <param name="judge">Asks the model.</param>
    /// <param name="evidenceReader">Reads what the model is shown.</param>
    /// <param name="egressGuard">Names the account whose posture the input is guarded under.</param>
    /// <param name="telemetry">Counts how each consultation ended.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public SpamModelStage(
        ISpamModelJudge judge,
        SpamModelEvidenceReader evidenceReader,
        SensitiveContentEgressGuard egressGuard,
        ISpamModelStageTelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(judge);
        ArgumentNullException.ThrowIfNull(evidenceReader);
        ArgumentNullException.ThrowIfNull(egressGuard);
        ArgumentNullException.ThrowIfNull(telemetry);

        this.judge = judge;
        this.evidenceReader = evidenceReader;
        this.egressGuard = egressGuard;
        this.telemetry = telemetry;
    }

    /// <summary>Composes the classification of one message from the deterministic reading and, where it is asked, the model.</summary>
    /// <param name="email">The occurrence being classified.</param>
    /// <param name="content">Its stored content.</param>
    /// <param name="reading">What the deterministic stage concluded.</param>
    /// <param name="settings">What the occurrence's account decided.</param>
    /// <param name="evaluatedAt">When the classification is evaluated.</param>
    /// <param name="cancellationToken">Withdraws the work.</param>
    /// <returns>The classification to record.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public async Task<SpamClassification> DecideAsync(
        ClassifiableEmail email,
        StoredEmailContent content,
        DeterministicSpamReading reading,
        SpamClassificationSettings settings,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(settings);

        if (JobFor(reading, settings.ModelStage) is not { } job)
        {
            return Deterministic(email, reading, settings.Profile, evaluatedAt);
        }

        if (!this.judge.IsActive)
        {
            this.telemetry.RecordConsultation(job, SpamModelOutcome.NotActivated);

            return Deterministic(email, reading, SpamClassificationProfile.Create(), evaluatedAt);
        }

        // A message whose stored content no renderer can read has nothing the model could be shown, and reading it again
        // later reads the same bytes, so it is recorded as decided under the account's terms rather than left for a run.
        if (await this.evidenceReader.ReadAsync(email.Id, content, cancellationToken) is not { } evidence)
        {
            return Deterministic(email, reading, settings.Profile, evaluatedAt);
        }

        SpamModelJudgement judgement;

        using (this.egressGuard.ActingFor(await this.egressGuard.ReadPostureOfAsync(email.AccountId, cancellationToken)))
        {
            judgement = await this.judge.JudgeAsync(evidence, job, cancellationToken);
        }

        this.telemetry.RecordConsultation(job, judgement.Outcome);

        return judgement.Answer is { } answer
            ? Judged(email, reading, settings, job, answer, evaluatedAt)
            : Deterministic(email, reading, SpamClassificationProfile.Create(), evaluatedAt);
    }

    /// <summary>Answers which job the model is asked to do, or that it is not asked at all.</summary>
    private static SpamModelJob? JobFor(DeterministicSpamReading reading, SpamModelStageSettings stage)
    {
        if (!stage.IsConsulted || CarriesTheNotJunkCorrection(reading))
        {
            return null;
        }

        if (reading.Verdict is not SpamVerdict.Spam)
        {
            return SpamModelJob.DecideVerdict;
        }

        return stage.NamesSubcategoriesOfDeterministicSpam ? SpamModelJob.NameSubcategory : null;
    }

    /// <summary>Answers whether somebody other than this deployment marked the message not junk.</summary>
    /// <remarks>
    /// Read from the deterministic stage's own signals, which already leave out every keyword this deployment wrote, so
    /// the stage never reads its own act back as the person's correction.
    /// </remarks>
    private static bool CarriesTheNotJunkCorrection(DeterministicSpamReading reading) =>
        reading.Signals.Any(static signal =>
            signal.Kind is SpamSignalKind.JunkKeyword
            && string.Equals(signal.Name, JunkKeywords.NotJunk, StringComparison.OrdinalIgnoreCase));

    private static SpamClassification Deterministic(
        ClassifiableEmail email,
        DeterministicSpamReading reading,
        SpamClassificationProfile profile,
        DateTimeOffset evaluatedAt) => SpamClassification.Create(
        email.Id,
        reading.Verdict,
        SpamClassificationStage.Deterministic,
        reading.Assessment,
        profile,
        reading.Signals,
        evaluatedAt);

    /// <summary>Composes the record a model's answer produces.</summary>
    /// <remarks>
    /// <para>
    /// On the first job the probability is judged against the account's threshold, and the verdict is the model's. A
    /// subcategory is recorded only on the spam verdict that judgement reaches; on the second job the deterministic verdict
    /// stays, decided by the deterministic stage, with the subcategory the model named beside it.
    /// </para>
    /// <para>
    /// What the record's invariants forbid is dropped rather than repaired: a subcategory below the threshold, a phishing
    /// request beside anything but phishing, and an impersonated party beside anything but phishing and business email
    /// compromise. The model's own signals are kept ahead of the bound on how many a record holds, so a message carrying
    /// many headers cannot push out the judgement its verdict rests on.
    /// </para>
    /// </remarks>
    private static SpamClassification Judged(
        ClassifiableEmail email,
        DeterministicSpamReading reading,
        SpamClassificationSettings settings,
        SpamModelJob job,
        SpamModelAnswer answer,
        DateTimeOffset evaluatedAt)
    {
        SpamSignal[] modelSignals =
        [
            .. answer.Choices.Select(choice => SpamSignal.Create(
                SpamSignalKind.ModelJudgement,
                choice.Question,
                choice.Option,
                SpamSignalProvenance.FromModel(answer.ModelBuild, answer.InstructionRevision))),
        ];
        SpamSignal[] signals =
        [
            .. reading.Signals.Take(Math.Max(0, SpamClassification.MaximumSignals - modelSignals.Length)),
            .. modelSignals,
        ];

        var (verdict, decidedBy, assessment) = job is SpamModelJob.DecideVerdict && answer.SpamProbability is { } probability
            ? ModelVerdict(SpamAssessment.Create(probability, settings.ModelStage.Threshold))
            : (reading.Verdict, SpamClassificationStage.Deterministic, reading.Assessment);

        var subcategory = verdict is SpamVerdict.Spam ? answer.Subcategory : (SpamSubcategory?)null;

        return SpamClassification.Create(
            email.Id,
            verdict,
            decidedBy,
            assessment,
            settings.Profile,
            signals,
            evaluatedAt,
            subcategory,
            subcategory is SpamSubcategory.Phishing ? answer.PhishingRequest : null,
            subcategory is SpamSubcategory.Phishing or SpamSubcategory.BusinessEmailCompromise ? answer.ImpersonatedParty : null);
    }

    private static (SpamVerdict Verdict, SpamClassificationStage DecidedBy, SpamAssessment Assessment) ModelVerdict(
        SpamAssessment assessment) => (
        assessment.ClearsThreshold ? SpamVerdict.Spam : SpamVerdict.NotSpam,
        SpamClassificationStage.Model,
        assessment);
}
