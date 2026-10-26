// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.AI.DecisionModels;
using MailFathom.AI.Decisions;
using MailFathom.AI.SpamClassification;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Provenance;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.Spam;
using MailFathom.Application.Spam.Model;
using MailFathom.Application.Spam.Signals;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;
using MailFathom.Evaluations.Answering;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Reporting;
using MailFathom.Infrastructure.Mail.Mime;
using MailFathom.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailFathom.Evaluations.SpamClassification;

/// <summary>Classifies every labelled message through the spam model stage under one decision model, and files what it recorded.</summary>
/// <remarks>
/// <para>
/// Everything between the stored message and the record is the deployment's own code: the header reader and the
/// deterministic stage decide which job the model is given, the evidence reader and the renderer it reads through choose
/// and bound what the model is shown, <see cref="SpamModelJudge" /> guards and writes the turn, asks the questions, and
/// reads the answer, and <see cref="SpamModelStage" /> judges the answer against the account's threshold and composes the
/// record — so a verdict here is about the questions, the minimised input, and the model, and nothing the scenario wrote.
/// The account consults the model for both jobs at <see cref="SpamModelStageSettings.DefaultThreshold" />, which is what
/// an account stating no threshold is judged against.
/// </para>
/// <para>
/// What is the scenario's own is what a deployment reads from its database: the provenance a message was recorded with,
/// an account that holds every message in its inbox, and a period ledger that admits every call. The model is reached
/// through the run's response cache, so a run that changed neither the questions, the input, nor the model asks it
/// nothing, and the messages are classified at once, bounded by the judge's own call slots as a deployment's backlog is.
/// </para>
/// </remarks>
internal static class SpamClassificationScenario
{
    /// <summary>The name the scenario is filed and reported under.</summary>
    public const string Name = "SpamClassification.LabelledMail";

    private static readonly MailAccountId Account = MailAccountId.Create("evaluation");

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("inbox");

    private static readonly RemoteEmailKeywords NoKeywords = RemoteEmailKeywords.Create(null);

    private static readonly DateTimeOffset EvaluatedAt = new(2026, 10, 20, 9, 30, 0, TimeSpan.Zero);

    private static readonly SpamClassificationSettings Settings = SpamClassificationSettings.Create(
        isEnabled: true,
        [Inbox],
        SpamModelStageSettings.Consulted(namesSubcategoriesOfDeterministicSpam: true, SpamModelStageSettings.DefaultThreshold));

    private static readonly EmailProvenance TrustedAuthor = new()
    {
        Sender = new SenderVerification
        {
            AuthorAuthentication = AuthorAuthenticationOutcome.Authenticated,
            DeploymentTrust = SenderTrustLevel.Trusted,
        },
        IsInJunkFolder = false,
        IsFirstContact = false,
    };

    /// <summary>Gets what every run of this scenario is measured on.</summary>
    public static IReadOnlyList<IEvaluator> Evaluators => [new SpamClassificationEvaluator()];

    /// <summary>Runs the scenario under one decision model and files the measurement in the run's store.</summary>
    /// <param name="reporting">The run's store, opened without a judge.</param>
    /// <param name="model">The decision model's client, which stays the caller's.</param>
    /// <param name="plan">The plan the model is measured with, whose alias is what the result is filed under.</param>
    /// <param name="repetition">Which repetition this is, counted from one, which the result and the cached answers are filed under.</param>
    /// <param name="modelSpend">What reaching that model has cost, which is the meter its client is opened over.</param>
    /// <param name="cancellationToken">Withdraws the run.</param>
    /// <returns>The metrics the measurement produced.</returns>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Disposing the caching wrapper would dispose the caller's model client, which this scenario does not own.")]
    public static async Task<EvaluationResult> RunAsync(
        ReportingConfiguration reporting,
        IDecisionClient model,
        DecisionModelPlan plan,
        int repetition,
        SpendMeter modelSpend,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reporting);
        ArgumentNullException.ThrowIfNull(plan);

        var iterationName = EvaluationStore.IterationNameFor(plan.Alias, repetition);

        await using var scenarioRun = await reporting.CreateScenarioRunAsync(
            Name,
            iterationName,
            cancellationToken: cancellationToken);

        var cachedModel = await EvaluationStore.CacheDecisionsOverAsync(reporting, model, plan, Name, iterationName, repetition, cancellationToken);
        var egressGuard = SensitiveContentEgressGuards.Inactive();

        await using var services = new ServiceCollection()
            .AddKeyedSingleton(DecisionFunction.SpamClassification, cachedModel)
            .AddSingleton<IMailAnsweringSpendLedger, NoPeriodSpendLedger>()
            .BuildServiceProvider();
        using var judge = new SpamModelJudge(
            services.GetRequiredService<IServiceScopeFactory>(),
            egressGuard,
            SpamModelStageBounds.Default,
            NullLogger<SpamModelJudge>.Instance);

        SpamCaseOutcome[] outcomes = await Task.WhenAll(
            SpamClassificationCase.All.Select(scenario => ClassifyAsync(judge, egressGuard, scenario, cancellationToken)));
        var measurement = new SpamClassificationMeasurement(outcomes);

        var verdict = await scenarioRun.EvaluateAsync(
            [new ChatMessage(ChatRole.User, DescribeCases())],
            new ChatResponse(new ChatMessage(ChatRole.Assistant, measurement.Summary)) { ModelId = plan.Alias },
            [measurement],
            cancellationToken);

        EvaluationCost.Record(verdict, plan.Alias, modelSpend.Take(), judgeSpend: default);

        return verdict;
    }

    /// <summary>Reads one stored message's headers and runs the deterministic stage over them, as a deployment does before the model stage.</summary>
    /// <param name="content">The stored message.</param>
    /// <param name="cancellationToken">Withdraws the read.</param>
    /// <returns>The headers read and what the deterministic stage concluded from them.</returns>
    internal static async Task<(SpamHeaderFacts Facts, DeterministicSpamReading Reading)> ReadDeterministicallyAsync(
        StoredEmailContent content,
        CancellationToken cancellationToken)
    {
        var facts = await new MimeKitEmailSpamHeaderReader().ReadAsync(content, cancellationToken);

        return (facts, new DeterministicSpamClassifier().Read(facts, Inbox, isJunkFolder: false, NoKeywords, new HashSet<SpamVerdictAct>()));
    }

    /// <summary>Classifies one message as a deployment would: its headers read, the deterministic stage run over them, and the model stage composed after it.</summary>
    private static async Task<SpamCaseOutcome> ClassifyAsync(
        SpamModelJudge judge,
        SensitiveContentEgressGuard egressGuard,
        SpamClassificationCase scenario,
        CancellationToken cancellationToken)
    {
        var content = scenario.Content();
        var (facts, reading) = await ReadDeterministicallyAsync(content, cancellationToken);
        var consultation = new RecordedConsultation();
        var stage = new SpamModelStage(
            judge,
            new SpamModelEvidenceReader(
                new MimeKitEmailContentRenderer(new EmailMimeExtractionOptions()),
                StubEmailProvenanceReader.Answering(scenario.Mail.IsSenderTrusted ? TrustedAuthor : StubEmailProvenanceReader.KnownCorrespondent),
                SpamModelStageBounds.Default),
            egressGuard,
            consultation);

        var recorded = await stage.DecideAsync(
            new ClassifiableEmail(scenario.Id, Account, Inbox, NoKeywords),
            content,
            facts,
            reading,
            Settings,
            EvaluatedAt,
            cancellationToken);

        return new SpamCaseOutcome(
            scenario,
            consultation.Job,
            consultation.Outcome,
            recorded.Verdict,
            recorded.Subcategory,
            recorded.DecidedBy is SpamClassificationStage.Model ? recorded.Assessment?.Score : null);
    }

    private static string DescribeCases() =>
        string.Join(
            '\n',
            SpamClassificationCase.All.Select(static scenario =>
                $"{scenario.Name} ({scenario.Language}, {scenario.Job}): {scenario.Mail.Subject}"));

    /// <summary>Keeps the one consultation a classification makes, which is how the stage reports a model that gave no answer.</summary>
    private sealed class RecordedConsultation : ISpamModelStageTelemetry
    {
        public SpamModelJob? Job { get; private set; }

        public SpamModelOutcome? Outcome { get; private set; }

        public void RecordConsultation(SpamModelJob job, SpamModelOutcome outcome) =>
            (this.Job, this.Outcome) = (job, outcome);
    }
}
