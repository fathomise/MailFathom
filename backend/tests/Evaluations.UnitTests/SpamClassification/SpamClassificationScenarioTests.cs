// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.RegularExpressions;
using MailFathom.AI.DecisionModels;
using MailFathom.AI.Decisions;
using MailFathom.AI.SpamClassification;
using MailFathom.Application.Spam.Model;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Spam;
using MailFathom.Evaluations.Corpus;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Languages;
using MailFathom.Evaluations.Reporting;
using MailFathom.Evaluations.SpamClassification;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Xunit;

namespace MailFathom.Evaluations.UnitTests.SpamClassification;

/// <summary>
/// Proves, without calling any provider, that the labelled messages are what their labels say, that the figures are read
/// off what the stage recorded, that each figure fails on the answer it exists to catch, and that a repeated run asks the
/// model nothing.
/// </summary>
/// <remarks>
/// Free, and therefore not gated on the run switch. Like the enrichment scenario's tests, these write a real store to a
/// temporary directory, because the scenario files its measurement there and caches its answers there.
/// </remarks>
public sealed partial class SpamClassificationScenarioTests : IDisposable
{
    private static readonly DecisionModelPlan Plan =
        new("model-under-test", Address: null, "model-under-test", TimeSpan.FromSeconds(30), 32_000);

    private readonly DirectoryInfo store = Directory.CreateTempSubdirectory("mailfathom-evaluations-");

    [Fact]
    public void All_EveryKind_IsLabelledOnAtLeastThreeMessages()
    {
        // Act
        var labelled = SpamClassificationEvaluator.Kinds.Select(kind => SpamClassificationCase.All.Count(scenario => scenario.Expected == kind));

        // Assert
        Assert.All(labelled, static count => Assert.True(count >= 3, $"A kind is labelled on {count} message(s)."));
    }

    [Fact]
    public async Task Content_EveryMessage_LeavesTheModelTheJobItsCaseDeclaresThroughTheDeterministicStage()
    {
        // Act
        var readings = await Task.WhenAll(SpamClassificationCase.All.Select(static async scenario =>
            (scenario.Job, (await SpamClassificationScenario.ReadDeterministicallyAsync(scenario.Content(), TestContext.Current.CancellationToken)).Reading.Verdict)));

        // Assert
        Assert.All(readings, static reading => Assert.Equal(reading.Job is SpamModelJob.NameSubcategory, reading.Verdict is SpamVerdict.Spam));
        Assert.Equal(Enum.GetValues<SpamModelJob>(), readings.Select(static reading => reading.Job).Distinct().Order());
    }

    [Theory]
    [InlineData("Legitimate.BankNotice")]
    [InlineData("Legitimate.Invoice")]
    [InlineData("Legitimate.PasswordResetRequested")]
    [InlineData("Legitimate.DeliveryNotice")]
    [InlineData("Legitimate.SubscribedNewsletter")]
    [InlineData("Polish.Legitimate.BankNotice")]
    [InlineData("Polish.Legitimate.Invoice")]
    [InlineData("Polish.Legitimate.PasswordResetRequested")]
    [InlineData("Polish.Legitimate.DeliveryNotice")]
    [InlineData("Polish.Legitimate.SubscribedNewsletter")]
    public void Named_AHardNegative_IsOrdinaryMailWhoseVerdictTheModelDecides(string caseName)
    {
        // Act
        var scenario = SpamClassificationCase.Named(caseName);

        // Assert
        Assert.Equal((true, SpamModelJob.DecideVerdict), (scenario.IsLegitimate, scenario.Job));
    }

    [Fact]
    public void All_EveryMessage_IsWrittenInTheLanguageItsCaseDeclares()
    {
        // Act
        var languages = SpamClassificationCase.All.Select(static scenario => (scenario.Name, scenario.Language, Written: WrittenLanguage.Of(scenario.Mail.Body)));

        // Assert
        Assert.All(languages, static language => Assert.True(language.Language == language.Written, $"{language.Name} reads as {language.Written}."));
        Assert.Equal(Enum.GetValues<MailAccountLanguage>(), SpamClassificationCase.All.Select(static scenario => scenario.Language).Distinct().Order());
    }

    [Fact]
    public void All_EveryAddressAndEveryLink_SitsUnderAReservedDomain()
    {
        // Act
        var addresses = SpamClassificationCase.All
            .SelectMany(static scenario => (string?[])[scenario.Mail.SenderAddress, scenario.Mail.ReplyTo, .. EmailAddress().Matches(scenario.Mail.Body).Select(static match => match.Value)])
            .OfType<string>()
            .ToArray();
        var hosts = SpamClassificationCase.All
            .SelectMany(static scenario => scenario.Mail.Links)
            .SelectMany(static link => (string[])[link.Target.Host, .. Uri.TryCreate(link.Text, UriKind.Absolute, out var named) ? [named.Host] : Array.Empty<string>()])
            .ToArray();

        // Assert
        Assert.NotEmpty(hosts);
        Assert.All(addresses, static address => Assert.Matches(ReservedAddress(), address));
        Assert.All(hosts, static host => Assert.Matches(ReservedHost(), host));
    }

    [Theory]
    [InlineData("Phishing.BankCallbackNoLink", "+44 20 7946 0958")]
    [InlineData("BusinessEmailCompromise.UrgentTransferNoLink", "48,500 EUR")]
    public void Named_APhishingByTelephoneOrABusinessEmailCompromise_CarriesNoLinkAtAll(string caseName, string evidence)
    {
        // Act
        var scenario = SpamClassificationCase.Named(caseName);

        // Assert
        Assert.Empty(scenario.Mail.Links);
        Assert.DoesNotContain("http", scenario.Mail.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(evidence, scenario.Mail.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void All_EveryHostileMessage_DictatesAnOptionItIsNotAndWritesThatOption()
    {
        // Act
        var hostile = SpamClassificationCase.All.Where(static scenario => scenario.DictatedOption is not null).ToArray();

        // Assert
        Assert.True(hostile.Length >= 3, $"{hostile.Length} hostile message(s).");
        Assert.All(hostile, static scenario =>
        {
            Assert.False(scenario.IsLegitimate);
            Assert.NotEqual(SpamCaseOutcome.OptionOf(scenario.Expected!.Value), scenario.DictatedOption);
            Assert.Contains(scenario.DictatedOption!, scenario.Mail.Body, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task RunAsync_AModelAnsweringEveryLabel_FailsNothingAndReportsNoFalsePositive()
    {
        // Arrange
        using var model = new LabelReadingDecisionClient(static scenario => scenario.Expected is { } kind ? SpamCaseOutcome.OptionOf(kind) : SpamClassificationQuestions.NotSpamOption);

        // Act
        var verdict = await this.RunScenarioAsync(model);

        // Assert
        Assert.Empty(FailedMetrics(verdict));
        Assert.Equal(0, verdict.Get<NumericMetric>(SpamClassificationEvaluator.FalsePositiveRateMetricName).Value);
        Assert.All(SpamClassificationEvaluator.Kinds, kind =>
            Assert.Equal((1d, 1d), (verdict.Get<NumericMetric>(SpamClassificationEvaluator.PrecisionName(kind)).Value, verdict.Get<NumericMetric>(SpamClassificationEvaluator.RecallName(kind)).Value)));
    }

    [Fact]
    public async Task RunAsync_AModelCallingLegitimateMailPhishing_FailsTheFalsePositiveRateAndThePrecisionOfPhishing()
    {
        // Arrange
        using var model = new LabelReadingDecisionClient(static scenario => SpamCaseOutcome.OptionOf(scenario.Expected ?? SpamSubcategory.Phishing));

        // Act
        var verdict = await this.RunScenarioAsync(model);

        // Assert
        Assert.Equal(
            [SpamClassificationEvaluator.FalsePositiveRateMetricName, SpamClassificationEvaluator.PrecisionName(SpamSubcategory.Phishing)],
            FailedMetrics(verdict));
        Assert.Equal(1, verdict.Get<NumericMetric>(SpamClassificationEvaluator.FalsePositiveRateMetricName).Value);
    }

    [Fact]
    public async Task RunAsync_AModelCallingEverythingOrdinaryMail_FailsTheRecallOfEveryKind()
    {
        // Arrange
        using var model = new LabelReadingDecisionClient(static _ => SpamClassificationQuestions.NotSpamOption);

        // Act
        var verdict = await this.RunScenarioAsync(model);

        // Assert
        Assert.All(SpamClassificationEvaluator.Kinds, kind =>
            Assert.True(verdict.Get<NumericMetric>(SpamClassificationEvaluator.RecallName(kind)).Interpretation?.Failed, $"The recall of {kind} held."));
        Assert.False(verdict.Get<NumericMetric>(SpamClassificationEvaluator.FalsePositiveRateMetricName).Interpretation?.Failed);
    }

    [Fact]
    public async Task RunAsync_AModelCallingEveryBusinessEmailCompromisePhishing_FailsThatRecallAndThePrecisionOfPhishing()
    {
        // Arrange
        using var model = new LabelReadingDecisionClient(static scenario => scenario.Expected switch
        {
            null => SpamClassificationQuestions.NotSpamOption,
            SpamSubcategory.BusinessEmailCompromise => SpamCaseOutcome.OptionOf(SpamSubcategory.Phishing),
            { } kind => SpamCaseOutcome.OptionOf(kind),
        });

        // Act
        var verdict = await this.RunScenarioAsync(model);

        // Assert
        Assert.Equal(
            [SpamClassificationEvaluator.PrecisionName(SpamSubcategory.Phishing), SpamClassificationEvaluator.RecallName(SpamSubcategory.BusinessEmailCompromise)],
            FailedMetrics(verdict));
    }

    [Fact]
    public async Task RunAsync_AModelObeyingTheMail_FailsTheCheckThatItObeysNoInstructionInTheMail()
    {
        // Arrange
        using var model = new LabelReadingDecisionClient(static scenario => scenario.DictatedOption
            ?? (scenario.Expected is { } kind ? SpamCaseOutcome.OptionOf(kind) : SpamClassificationQuestions.NotSpamOption));

        // Act
        var verdict = await this.RunScenarioAsync(model);

        // Assert
        var obeys = verdict.Get<BooleanMetric>(HostileMail.ObeysNoMailMetricName);

        Assert.False(obeys.Value);
        Assert.All(
            SpamClassificationCase.All.Where(static scenario => scenario.DictatedOption is not null),
            scenario => Assert.Contains(scenario.Name, obeys.Reason, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_AProviderThatCannotBeReached_FailsTheCheckThatEveryMessageWasAnswered()
    {
        // Arrange
        using var model = new LabelReadingDecisionClient(static _ => throw new DecisionFailedException(Plan.Alias, DecisionFailure.TransportFaulted));

        // Act
        var verdict = await this.RunScenarioAsync(model);

        // Assert
        var answered = verdict.Get<BooleanMetric>(SpamClassificationEvaluator.AnsweredMetricName);

        Assert.False(answered.Value);
        Assert.Contains(nameof(SpamModelOutcome.ProviderUnavailable), answered.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_AgainOverUnchangedQuestionsAndModel_AsksTheModelOncePerMessageAndReportsNothingSpent()
    {
        // Arrange
        using var model = new LabelReadingDecisionClient(static scenario => scenario.Expected is { } kind ? SpamCaseOutcome.OptionOf(kind) : SpamClassificationQuestions.NotSpamOption);

        await this.RunScenarioAsync(model, executionName: "first");

        // Act
        var repeated = await this.RunScenarioAsync(model, executionName: "second");

        // Assert
        Assert.Equal(SpamClassificationCase.All.Count, model.Requests);
        Assert.Empty(FailedMetrics(repeated));
        Assert.Equal("0", repeated.Get<NumericMetric>(EvaluationCost.MetricName).Metadata?["paid-calls"]);
    }

    public void Dispose() => this.store.Delete(recursive: true);

    private static string[] FailedMetrics(EvaluationResult verdict) =>
    [
        .. verdict.Metrics.Values
            .Where(static metric => metric.Interpretation is { Failed: true })
            .Select(static metric => metric.Name)
            .Order(StringComparer.Ordinal),
    ];

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailAddress();

    /// <summary>An address under a domain RFC 2606 or RFC 6761 sets aside.</summary>
    [GeneratedRegex(@"@(?:[A-Za-z0-9-]+\.)*(?:test|example|invalid|example\.(?:com|net|org))$", RegexOptions.IgnoreCase)]
    private static partial Regex ReservedAddress();

    /// <summary>A host under a domain RFC 2606 or RFC 6761 sets aside.</summary>
    [GeneratedRegex(@"^(?:[A-Za-z0-9-]+\.)*(?:test|example|invalid|example\.(?:com|net|org))$", RegexOptions.IgnoreCase)]
    private static partial Regex ReservedHost();

    private async Task<EvaluationResult> RunScenarioAsync(IDecisionClient model, string executionName = "only")
    {
        var reporting = EvaluationStore.OpenUnjudgedAt(this.store.FullName, executionName, SpamClassificationScenario.Evaluators);

        return await SpamClassificationScenario.RunAsync(
            reporting,
            model,
            Plan,
            repetition: 1,
            new SpendMeter(),
            TestContext.Current.CancellationToken);
    }

    /// <summary>A decision model that reads which labelled message it was shown, by its subject, and chooses what a policy says for it.</summary>
    /// <remarks>
    /// The policy names an option of the question deciding the kind; where that question does not offer it — the job that
    /// only names a kind offers no <c>not_spam</c> — the first option offered is chosen instead, as a model has to choose
    /// one. The two detail questions are answered with their last option, which names nothing.
    /// </remarks>
    private sealed class LabelReadingDecisionClient(Func<SpamClassificationCase, string> choose) : IDecisionClient
    {
        private int requests;

        public int Requests => this.requests;

        public Task<DecisionResponse> DecideAsync(
            IEnumerable<ChatMessage> input,
            IReadOnlyList<DecisionQuestion> questions,
            DecisionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref this.requests);

            var turn = string.Concat(input.Select(static message => message.Text));
            var scenario = SpamClassificationCase.All.Single(candidate => turn.Contains(candidate.Mail.Subject, StringComparison.Ordinal));
            var chosen = choose(scenario);

            return Task.FromResult(new DecisionResponse(
            [
                Answer(questions[0], questions[0].Options.Any(option => option.Value == chosen) ? chosen : questions[0].Options[0].Value),
                .. questions.Skip(1).Select(static question => Answer(question, question.Options[^1].Value)),
            ]));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }

        private static DecisionAnswer Answer(DecisionQuestion question, string chosen) => new(
            question.Id,
            chosen,
            question.Options.ToDictionary(
                static option => option.Value,
                option => option.Value == chosen ? 0.9 : 0.1 / (question.Options.Count - 1),
                StringComparer.Ordinal));
    }
}
