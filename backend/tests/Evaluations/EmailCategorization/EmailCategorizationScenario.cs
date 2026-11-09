// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MailFathom.AI.DecisionModels;
using MailFathom.AI.Decisions;
using MailFathom.AI.EmailCategorization;
using MailFathom.Application.Categories;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.Spam.Model;
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

namespace MailFathom.Evaluations.EmailCategorization;

/// <summary>Measures categorization over labelled mail, through the production evidence reader, judge, questions, and reading.</summary>
internal static class EmailCategorizationScenario
{
    /// <summary>The name the scenario is filed and reported under.</summary>
    public const string Name = "EmailCategorization.LabelledMail";

    /// <summary>Gets what every run is measured on.</summary>
    public static IReadOnlyList<IEvaluator> Evaluators => [new EmailCategorizationEvaluator()];

    /// <summary>Runs the scenario over every labelled message under one decision model and files the measurement in the run's store.</summary>
    /// <param name="reporting">The run's store, opened without a judge and over <see cref="Evaluators" />.</param>
    /// <param name="model">The decision model's client, which stays the caller's.</param>
    /// <param name="plan">The plan the model is measured with.</param>
    /// <param name="repetition">Which repetition this is, counted from one.</param>
    /// <param name="modelSpend">What reaching that model has cost.</param>
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
            additionalTags: EvaluationStore.TagsFor(Name, plan.Alias),
            cancellationToken: cancellationToken);

        var cachedModel = await EvaluationStore.CacheDecisionsOverAsync(reporting, model, plan, Name, iterationName, repetition, cancellationToken);
        var egressGuard = SensitiveContentEgressGuards.Inactive();

        await using var services = new ServiceCollection()
            .AddKeyedSingleton(DecisionFunction.EmailCategorization, cachedModel)
            .AddSingleton<IMailAnsweringSpendLedger, NoPeriodSpendLedger>()
            .BuildServiceProvider();
        using var judge = new EmailCategoryJudge(
            services.GetRequiredService<IServiceScopeFactory>(),
            egressGuard,
            SpamModelStageBounds.Default,
            plan.MaximumRequestCharacters,
            NullLogger<EmailCategoryJudge>.Instance);
        var evidenceReader = new SpamModelEvidenceReader(
            new MimeKitEmailContentRenderer(new EmailMimeExtractionOptions()),
            StubEmailProvenanceReader.AllKnownCorrespondents,
            SpamModelStageBounds.Default);

        var outcomes = new List<EmailCategoryCaseOutcome>(EmailCategorizationCase.All.Count);

        foreach (var scenario in EmailCategorizationCase.All)
        {
            outcomes.Add(await CategorizeAsync(judge, evidenceReader, scenario, cancellationToken));
        }

        var inputCounts = await CountInputAsync(cachedModel, evidenceReader, egressGuard, plan, cancellationToken);
        var measurement = new EmailCategorizationMeasurement(outcomes, inputCounts);

        var verdict = await scenarioRun.EvaluateAsync(
            [new ChatMessage(ChatRole.User, string.Join('\n', EmailCategorizationCase.All.Select(static scenario => $"{scenario.Name} ({scenario.Language}): {scenario.Subject}")))],
            new ChatResponse(new ChatMessage(ChatRole.Assistant, measurement.Summary)) { ModelId = plan.Alias },
            [measurement],
            cancellationToken);

        EvaluationCost.Record(verdict, plan.Alias, modelSpend.Take(), judgeSpend: default);

        return verdict;
    }

    private static async Task<EmailCategoryCaseOutcome> CategorizeAsync(
        EmailCategoryJudge judge,
        SpamModelEvidenceReader evidenceReader,
        EmailCategorizationCase scenario,
        CancellationToken cancellationToken)
    {
        var evidence = await evidenceReader.ReadAsync(scenario.Id, scenario.Content(), cancellationToken)
            ?? throw new InvalidOperationException($"The renderer read nothing out of '{scenario.Name}'.");

        var judgement = await judge.JudgeAsync(
            new EmailCategoryEvidence(evidence, scenario.Opening),
            EmailCategorizationCase.OwnCategories,
            cancellationToken);

        return new EmailCategoryCaseOutcome(scenario, judgement.Outcome, judgement.Assignments);
    }

    /// <summary>Asks about one fixed message under each number of own categories, so what a request is counted at can be read against the limit one account may hold.</summary>
    private static async Task<IReadOnlyList<(int OwnCategories, long? InputTokens)>> CountInputAsync(
        IDecisionClient model,
        SpamModelEvidenceReader evidenceReader,
        SensitiveContentEgressGuard egressGuard,
        DecisionModelPlan plan,
        CancellationToken cancellationToken)
    {
        var scenario = EmailCategorizationCase.All[0];
        var evidence = await evidenceReader.ReadAsync(scenario.Id, scenario.Content(), cancellationToken)
            ?? throw new InvalidOperationException($"The renderer read nothing out of '{scenario.Name}'.");

        var counts = new List<(int, long?)>(EmailCategorizationEvaluator.CountedOwnCategories.Count);

        foreach (var ownCategories in EmailCategorizationEvaluator.CountedOwnCategories)
        {
            DecisionQuestion[] questions =
            [
                EmailCategoryQuestions.Default,
                .. Enumerable.Range(0, ownCategories).Select(static index => EmailCategoryQuestions.For(new OwnMailCategory(
                    string.Create(CultureInfo.InvariantCulture, $"Subject {index}"),
                    string.Create(CultureInfo.InvariantCulture, $"Mail about the subject numbered {index}, which is a matter this mailbox tracks: its people, its dates, and its costs.")))),
            ];
            var input = await EmailCategoryTurn.ComposeAsync(
                new EmailCategoryEvidence(evidence, ConversationOpening: null),
                egressGuard,
                Math.Max(0, plan.MaximumRequestCharacters - DecisionQuestionSet.ReservedCharactersOf(questions)),
                cancellationToken);

            counts.Add((ownCategories, await CountedInputTokensAsync(model, input, questions, cancellationToken)));
        }

        return counts;
    }

    /// <summary>Asks once and reads what the provider counted, with a provider that failed reading as no count, which the answered check already fails.</summary>
    private static async Task<long?> CountedInputTokensAsync(
        IDecisionClient model,
        string input,
        IReadOnlyList<DecisionQuestion> questions,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await model.DecideAsync([new ChatMessage(ChatRole.User, input)], questions, options: null, cancellationToken);

            return response.Usage?.InputTokenCount;
        }
        catch (DecisionFailedException)
        {
            return null;
        }
    }
}
