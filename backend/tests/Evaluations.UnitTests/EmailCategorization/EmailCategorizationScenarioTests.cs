// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.RegularExpressions;
using MailFathom.AI.DecisionModels;
using MailFathom.AI.Decisions;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Categories;
using MailFathom.Evaluations.Corpus;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.EmailCategorization;
using MailFathom.Evaluations.Reporting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Xunit;

namespace MailFathom.Evaluations.UnitTests.EmailCategorization;

/// <summary>Proves, without calling any provider, that the labelled messages are usable and that each figure fails on the answer it exists to catch.</summary>
public sealed partial class EmailCategorizationScenarioTests : IDisposable
{
    private static readonly DecisionModelPlan Plan =
        new("model-under-test", Address: null, "model-under-test", TimeSpan.FromSeconds(30), 32_000);

    private readonly DirectoryInfo store = Directory.CreateTempSubdirectory("mailfathom-evaluations-");

    public void Dispose() => this.store.Delete(recursive: true);

    [Fact]
    public void All_EveryDefault_IsLabelledOnAtLeastThreeMessagesInBothLanguages()
    {
        // Act & Assert
        Assert.All(DefaultMailCategory.All, category =>
        {
            var labelled = EmailCategorizationCase.All.Where(scenario => scenario.Expected == category).ToArray();

            Assert.True(labelled.Length >= 3, $"{category} has {labelled.Length} messages.");
            Assert.Contains(labelled, static scenario => scenario.Language == MailAccountLanguage.Polish);
            Assert.Contains(labelled, static scenario => scenario.Language == MailAccountLanguage.English);
        });
    }

    [Fact]
    public void All_EverySubject_IsUniqueSoAScriptedModelCanTellThemApart()
    {
        // Act & Assert
        Assert.Equal(
            EmailCategorizationCase.All.Count,
            EmailCategorizationCase.All.Select(static scenario => scenario.Subject).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void All_EveryAddress_SitsUnderAReservedDomain()
    {
        // Act & Assert
        Assert.All(EmailCategorizationCase.All, static scenario => Assert.Matches(ReservedAddress(), scenario.SenderAddress));
    }

    [Fact]
    public void All_EveryHostileMessage_DictatesAnOptionItIsNotAndWritesThatOption()
    {
        // Arrange
        var hostile = EmailCategorizationCase.All.Where(static scenario => scenario.Dictated is not null).ToArray();

        // Act & Assert
        Assert.NotEmpty(hostile);
        Assert.All(hostile, static scenario =>
        {
            Assert.NotEqual(scenario.Expected, scenario.Dictated);
            Assert.Contains(scenario.Dictated!.Value.ToString(), scenario.Body, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task RunAsync_AModelAnsweringEveryLabel_FailsNothingAndReportsWhatTheQuestionsWereCountedAt()
    {
        // Arrange
        using var model = new LabelReadingDecisionClient(
            static scenario => scenario.Expected,
            static (scenario, description) => scenario.Matches.Any(own => own.Description == description));

        // Act
        var verdict = await this.RunScenarioAsync(model);

        // Assert
        Assert.Empty(FailedMetrics(verdict));
        Assert.Equal(0, verdict.Get<NumericMetric>(EmailCategorizationEvaluator.OwnFalseMatchRateMetricName).Value);
        Assert.Equal(1, verdict.Get<NumericMetric>(EmailCategorizationEvaluator.OwnRecallMetricName).Value);
        Assert.All(EmailCategorizationEvaluator.CountedOwnCategories, count =>
            Assert.Equal(100d + count, verdict.Get<NumericMetric>(EmailCategorizationEvaluator.InputTokensName(count)).Value));
    }

    [Fact]
    public async Task RunAsync_AModelCallingEverythingOther_FailsTheRecallOfEveryOtherDefault()
    {
        // Arrange
        using var model = new LabelReadingDecisionClient(static _ => DefaultMailCategory.Other, static (_, _) => false);

        // Act
        var verdict = await this.RunScenarioAsync(model);

        // Assert
        Assert.All(
            DefaultMailCategory.All.Where(static category => category != DefaultMailCategory.Other),
            category => Assert.Contains(EmailCategorizationEvaluator.RecallName(category), FailedMetrics(verdict)));
    }

    [Fact]
    public async Task RunAsync_AModelObeyingTheMail_FailsTheCheckThatItObeysNoInstructionInTheMail()
    {
        // Arrange
        using var model = new LabelReadingDecisionClient(
            static scenario => scenario.Dictated ?? scenario.Expected,
            static (scenario, description) => scenario.Matches.Any(own => own.Description == description));

        // Act
        var verdict = await this.RunScenarioAsync(model);

        // Assert
        Assert.Contains(HostileMail.ObeysNoMailMetricName, FailedMetrics(verdict));
    }

    [Fact]
    public async Task RunAsync_AModelMatchingEveryOwnCategory_FailsTheFalseMatchRate()
    {
        // Arrange
        using var model = new LabelReadingDecisionClient(static scenario => scenario.Expected, static (_, _) => true);

        // Act
        var verdict = await this.RunScenarioAsync(model);

        // Assert
        Assert.Contains(EmailCategorizationEvaluator.OwnFalseMatchRateMetricName, FailedMetrics(verdict));
    }

    [Fact]
    public async Task RunAsync_AProviderThatCannotBeReached_FailsTheAnsweredCheck()
    {
        // Arrange
        using var model = new UnreachableDecisionClient();

        // Act
        var verdict = await this.RunScenarioAsync(model);

        // Assert
        Assert.Contains(EmailCategorizationEvaluator.AnsweredMetricName, FailedMetrics(verdict));
    }

    private static IReadOnlyList<string> FailedMetrics(EvaluationResult verdict) =>
    [
        .. verdict.Metrics.Values
            .Where(static metric => metric.Interpretation is { Failed: true })
            .Select(static metric => metric.Name),
    ];

    /// <summary>An address under a domain RFC 2606 or RFC 6761 sets aside.</summary>
    [GeneratedRegex(@"@(?:[A-Za-z0-9-]+\.)*(?:test|example|invalid|example\.(?:com|net|org))$", RegexOptions.IgnoreCase)]
    private static partial Regex ReservedAddress();

    private async Task<EvaluationResult> RunScenarioAsync(IDecisionClient model)
    {
        var reporting = EvaluationStore.OpenUnjudgedAt(this.store.FullName, "only", EmailCategorizationScenario.Evaluators);

        return await EmailCategorizationScenario.RunAsync(
            reporting,
            model,
            Plan,
            repetition: 1,
            new SpendMeter(),
            TestContext.Current.CancellationToken);
    }

    /// <summary>A decision model that reads which labelled message it was shown, by its subject, and answers what a policy says for it.</summary>
    private sealed class LabelReadingDecisionClient(
        Func<EmailCategorizationCase, DefaultMailCategory> choose,
        Func<EmailCategorizationCase, string, bool> matches) : IDecisionClient
    {
        public Task<DecisionResponse> DecideAsync(
            IEnumerable<ChatMessage> input,
            IReadOnlyList<DecisionQuestion> questions,
            DecisionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var turn = string.Concat(input.Select(static message => message.Text));
            var scenario = EmailCategorizationCase.All.Single(candidate => turn.Contains(candidate.Subject, StringComparison.Ordinal));
            var chosen = choose(scenario).ToString();

            return Task.FromResult(new DecisionResponse(
            [
                new DecisionAnswer(
                    questions[0].Id,
                    chosen,
                    questions[0].Options.ToDictionary(static option => option.Value, option => option.Value == chosen ? 0.9 : 0.1 / 6, StringComparer.Ordinal)),
                .. questions.Skip(1).Select(question => YesNo(question, matches(scenario, question.Options[0].Description))),
            ])
            {
                Usage = new UsageDetails { InputTokenCount = 100 + questions.Count - 1 },
            });
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }

        private static DecisionAnswer YesNo(DecisionQuestion question, bool yes) => new(
            question.Id,
            yes ? DecisionQuestion.Yes : DecisionQuestion.No,
            new Dictionary<string, double> { [DecisionQuestion.Yes] = yes ? 0.9 : 0.1, [DecisionQuestion.No] = yes ? 0.1 : 0.9 });
    }

    private sealed class UnreachableDecisionClient : IDecisionClient
    {
        public Task<DecisionResponse> DecideAsync(
            IEnumerable<ChatMessage> input,
            IReadOnlyList<DecisionQuestion> questions,
            DecisionOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new DecisionFailedException("model-under-test", DecisionFailure.TransportFaulted);

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
