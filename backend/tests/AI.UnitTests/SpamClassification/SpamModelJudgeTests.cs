// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.DecisionModels;
using MailFathom.AI.Decisions;
using MailFathom.AI.SpamClassification;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.Spam.Model;
using MailFathom.Domain.Spam;
using MailFathom.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MailFathom.AI.UnitTests.SpamClassification;

/// <summary>Covers what one judgement sends, what it makes of the answer, and the outcome every failure becomes instead of an exception.</summary>
/// <remarks>
/// The judge is composed over a real container, so each judgement opens its own scope and resolves the decision client
/// routed to spam classification there, exactly as a deployment does; the client is a script and nothing leaves.
/// </remarks>
public sealed class SpamModelJudgeTests
{
    private static readonly string[] MessageFragments = ["kestrel", "heron", "HERON-4417", "remittance"];

    [Fact]
    public void IsActive_AJudgeRegisteredForADeclaredModel_SaysSo()
    {
        // Arrange
        using var client = ScriptedDecisionClient.AnsweringFirstOption();
        using var judge = Judging(client);

        // Act & Assert
        Assert.True(judge.IsActive);
    }

    [Fact]
    public async Task JudgeAsync_AModelThatAnswered_ReturnsItsReading()
    {
        // Arrange
        using var client = ScriptedDecisionClient.Answering((_, _) => Task.FromResult(PhishingVerdict()));
        using var judge = Judging(client);

        // Act
        var judgement = await judge.JudgeAsync(SpamClassificationTurnTests.Evidence(), SpamModelJob.DecideVerdict, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamModelOutcome.Answered, judgement.Outcome);
        Assert.NotNull(judgement.Answer);
        Assert.Equal(SpamSubcategory.Phishing, judgement.Answer.Subcategory);
        Assert.Equal(0.75, judgement.Answer.SpamProbability);
        Assert.Equal("vendor/decider-2026-10-01", judgement.Answer.ModelBuild);
    }

    /// <summary>What leaves is the message's turn and the questions of the job it was asked for, and nothing else.</summary>
    [Theory]
    [InlineData(SpamModelJob.DecideVerdict)]
    [InlineData(SpamModelJob.NameSubcategory)]
    public async Task JudgeAsync_AnyJob_AsksThatJobsQuestionsAboutTheComposedTurn(SpamModelJob job)
    {
        // Arrange
        using var client = ScriptedDecisionClient.AnsweringFirstOption();
        using var judge = Judging(client);
        var evidence = SpamClassificationTurnTests.Evidence();

        // Act
        await judge.JudgeAsync(evidence, job, TestContext.Current.CancellationToken);

        // Assert
        var call = Assert.Single(client.Calls);
        Assert.Same(SpamClassificationQuestions.For(job), call.Questions);
        var input = Assert.Single(call.Input);
        Assert.Equal(ChatRole.User, input.Role);
        Assert.Equal(SpamClassificationTurn.Compose(evidence), input.Text);
    }

    /// <summary>A period already spent costs nothing to refuse, so nothing is guarded, composed, or sent.</summary>
    [Fact]
    public async Task JudgeAsync_APeriodThatHasSpentItsAllowance_IsBudgetExhaustedWithoutReachingTheModel()
    {
        // Arrange
        using var client = ScriptedDecisionClient.AnsweringFirstOption();
        using var judge = Judging(client, admitted: false);

        // Act
        var judgement = await judge.JudgeAsync(SpamClassificationTurnTests.Evidence(), SpamModelJob.DecideVerdict, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamModelOutcome.BudgetExhausted, judgement.Outcome);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task JudgeAsync_ACallTheRunCeilingStopped_IsBudgetExhausted()
    {
        // Arrange
        using var client = ScriptedDecisionClient.Throwing(MailAnsweringBudgetExhaustedException.RunSpent());
        using var judge = Judging(client);

        // Act
        var judgement = await judge.JudgeAsync(SpamClassificationTurnTests.Evidence(), SpamModelJob.DecideVerdict, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamModelOutcome.BudgetExhausted, judgement.Outcome);
        Assert.Null(judgement.Answer);
    }

    /// <summary>An answer outside the closed set is a contract that moved, and every other failure is the provider being unavailable.</summary>
    [Theory]
    [InlineData(DecisionFailure.AnswerUnreadable, SpamModelOutcome.AnswerUnreadable)]
    [InlineData(DecisionFailure.CredentialRejected, SpamModelOutcome.ProviderUnavailable)]
    [InlineData(DecisionFailure.RateLimited, SpamModelOutcome.ProviderUnavailable)]
    [InlineData(DecisionFailure.RequestTimedOut, SpamModelOutcome.ProviderUnavailable)]
    [InlineData(DecisionFailure.TransportFaulted, SpamModelOutcome.ProviderUnavailable)]
    [InlineData(DecisionFailure.RequestRefused, SpamModelOutcome.ProviderUnavailable)]
    public async Task JudgeAsync_ADecisionThatFailed_EndsInTheOutcomeItsFailureNames(DecisionFailure failure, SpamModelOutcome expected)
    {
        // Arrange
        using var client = ScriptedDecisionClient.Throwing(new DecisionFailedException("spam-decider", failure));
        using var judge = Judging(client);

        // Act
        var judgement = await judge.JudgeAsync(SpamClassificationTurnTests.Evidence(), SpamModelJob.DecideVerdict, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expected, judgement.Outcome);
    }

    /// <summary>The adapter refuses a request too large for the declared model as an argument it names <c>input</c>, which is permanent for the message.</summary>
    [Fact]
    public async Task JudgeAsync_AnInputTheModelCannotTake_IsInputTooLarge()
    {
        // Arrange
        using var client = ScriptedDecisionClient.Throwing(new ArgumentException("Too large for the declared model.", "input"));
        using var judge = Judging(client);

        // Act
        var judgement = await judge.JudgeAsync(SpamClassificationTurnTests.Evidence(), SpamModelJob.DecideVerdict, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamModelOutcome.InputTooLarge, judgement.Outcome);
    }

    [Fact]
    public async Task JudgeAsync_AScannerThatCannotBeReached_IsScannerUnavailableWithoutReachingTheModel()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Unavailable(TimeProvider.System);
        using var actingFor = egress.Guard.ActingFor(
            await egress.Guard.ReadPostureOfAsync(ScanningSensitiveContentEgress.Account, TestContext.Current.CancellationToken));
        using var client = ScriptedDecisionClient.AnsweringFirstOption();
        using var judge = Judging(client, egressGuard: egress.Guard);

        // Act
        var judgement = await judge.JudgeAsync(SpamClassificationTurnTests.Evidence(), SpamModelJob.DecideVerdict, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamModelOutcome.ScannerUnavailable, judgement.Outcome);
        Assert.Empty(client.Calls);
    }

    /// <summary>An answer the adapter accepted but which names no kind is refused by the reading rather than taken as ordinary mail.</summary>
    [Fact]
    public async Task JudgeAsync_AnAnswerWithoutTheKind_IsAnswerUnreadable()
    {
        // Arrange
        using var client = ScriptedDecisionClient.Answering((_, _) => Task.FromResult(new DecisionResponse([])));
        using var judge = Judging(client);

        // Act
        var judgement = await judge.JudgeAsync(SpamClassificationTurnTests.Evidence(), SpamModelJob.DecideVerdict, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamModelOutcome.AnswerUnreadable, judgement.Outcome);
        Assert.Null(judgement.Answer);
    }

    /// <summary>A withdrawn call is the caller's, not a provider failing, so it is never recorded as an outcome.</summary>
    [Fact]
    public async Task JudgeAsync_ACallerWithdrawingWhileTheCallIsInFlight_PropagatesTheCancellation()
    {
        // Arrange
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = ScriptedDecisionClient.Answering(async (_, cancellationToken) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            throw new InvalidOperationException("The stall was never meant to end.");
        });
        using var judge = Judging(client);
        var judging = judge.JudgeAsync(SpamClassificationTurnTests.Evidence(), SpamModelJob.DecideVerdict, cancellation.Token);
        await entered.Task;

        // Act
        await cancellation.CancelAsync();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => judging);
    }

    [Fact]
    public async Task JudgeAsync_ACallThatWasWithdrawn_GivesItsSlotBackToTheNextJudgement()
    {
        // Arrange
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;
        using var client = ScriptedDecisionClient.Answering(async (_, cancellationToken) =>
        {
            if (Interlocked.Increment(ref callCount) > 1)
            {
                return PhishingVerdict();
            }

            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            throw new InvalidOperationException("The stall was never meant to end.");
        });
        using var judge = Judging(client, maximumConcurrentCalls: 1);
        var withdrawn = judge.JudgeAsync(SpamClassificationTurnTests.Evidence(), SpamModelJob.DecideVerdict, cancellation.Token);
        await entered.Task;
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => withdrawn);

        // Act
        var judgement = await judge.JudgeAsync(SpamClassificationTurnTests.Evidence(), SpamModelJob.DecideVerdict, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamModelOutcome.Answered, judgement.Outcome);
    }

    /// <summary>The replica's bound is what keeps a classification backlog from opening as many calls as the queue has workers.</summary>
    [Fact]
    public async Task JudgeAsync_OneSlotAndACallInFlight_HoldsTheNextCallUntilTheFirstEnds()
    {
        // Arrange
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = HoldingEveryCallUntil(release.Task, out var entries);
        using var judge = Judging(client, maximumConcurrentCalls: 1);
        var first = judge.JudgeAsync(SpamClassificationTurnTests.Evidence(), SpamModelJob.DecideVerdict, TestContext.Current.CancellationToken);
        await entries[0].Task;

        // Act
        var second = judge.JudgeAsync(SpamClassificationTurnTests.Evidence(), SpamModelJob.DecideVerdict, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(client.Calls);
        Assert.False(second.IsCompleted);

        release.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(2, client.Calls.Count);
    }

    /// <summary>The control for the bound above: with a second slot the same second call is in flight beside the first.</summary>
    [Fact]
    public async Task JudgeAsync_TwoSlotsAndACallInFlight_SendsTheNextCallAtOnce()
    {
        // Arrange
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = HoldingEveryCallUntil(release.Task, out var entries);
        using var judge = Judging(client, maximumConcurrentCalls: 2);
        var first = judge.JudgeAsync(SpamClassificationTurnTests.Evidence(), SpamModelJob.DecideVerdict, TestContext.Current.CancellationToken);
        await entries[0].Task;

        // Act
        var second = judge.JudgeAsync(SpamClassificationTurnTests.Evidence(), SpamModelJob.DecideVerdict, TestContext.Current.CancellationToken);
        await entries[1].Task;

        // Assert
        Assert.Equal(2, client.Calls.Count);

        release.SetResult();
        await Task.WhenAll(first, second);
    }

    /// <summary>One message is one run, so each judgement resolves a client, and the ceiling it carries, of its own.</summary>
    [Fact]
    public async Task JudgeAsync_TwoJudgements_ResolveAClientInAScopeOfTheirOwn()
    {
        // Arrange
        var clients = new List<ScriptedDecisionClient>();
        var services = Services(admitted: true);
        services.AddKeyedScoped<IDecisionClient>(DecisionFunction.SpamClassification, (_, _) =>
        {
            var client = ScriptedDecisionClient.AnsweringFirstOption();
            clients.Add(client);

            return client;
        });
        using var provider = services.BuildServiceProvider();
        using var judge = Judge(provider, SensitiveContentEgressGuards.Inactive(), maximumConcurrentCalls: 1, NullLogger<SpamModelJudge>.Instance);

        // Act
        await judge.JudgeAsync(SpamClassificationTurnTests.Evidence(), SpamModelJob.DecideVerdict, TestContext.Current.CancellationToken);
        await judge.JudgeAsync(SpamClassificationTurnTests.Evidence(), SpamModelJob.NameSubcategory, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([1, 1], clients.Select(client => client.Calls.Count));
        Assert.All(clients, client => Assert.True(client.IsDisposed));
    }

    /// <summary>Somebody's mail is judged here, so whatever happens to a judgement, none of that mail reaches a log line.</summary>
    [Fact]
    public async Task JudgeAsync_AnsweredAndUnanswered_LogsNothingOfTheMessage()
    {
        // Arrange
        using var loggerFactory = new RecordingLoggerFactory();
        var logger = new Logger<SpamModelJudge>(loggerFactory);
        var evidence = SpamClassificationTurnTests.Evidence() with
        {
            SenderAddress = "kestrel.finance@example.com",
            DifferingReplyToAddress = "heron.payments@example.net",
            Subject = "Overdue remittance HERON-4417",
            Body = "Please settle remittance HERON-4417 today.",
        };
        using var answeringClient = ScriptedDecisionClient.Answering((_, _) => Task.FromResult(PhishingVerdict()));
        using var failingClient = ScriptedDecisionClient.Throwing(new DecisionFailedException("spam-decider", DecisionFailure.RequestRefused));
        using var answering = Judging(answeringClient, logger: logger);
        using var failing = Judging(failingClient, logger: logger);

        // Act
        await answering.JudgeAsync(evidence, SpamModelJob.DecideVerdict, TestContext.Current.CancellationToken);
        await failing.JudgeAsync(evidence, SpamModelJob.NameSubcategory, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, loggerFactory.Records.Count);
        Assert.DoesNotContain(
            loggerFactory.Records,
            record => MessageFragments.Any(fragment =>
                record.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase)
                || record.Properties.Values.Any(value => value?.ToString()?.Contains(fragment, StringComparison.OrdinalIgnoreCase) is true)));
    }

    private static DecisionResponse PhishingVerdict() => new(
        [
            new DecisionAnswer(
                SpamClassificationQuestions.VerdictQuestionId,
                "phishing",
                new Dictionary<string, double> { ["not_spam"] = 0.25, ["phishing"] = 0.75 }),
        ])
    {
        ModelId = "vendor/decider-2026-10-01",
    };

    /// <summary>Builds a client whose every call waits for one release, and signals as each call arrives.</summary>
    private static ScriptedDecisionClient HoldingEveryCallUntil(Task release, out TaskCompletionSource[] entries)
    {
        var arrivals = new[]
        {
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var callCount = 0;
        entries = arrivals;

        return ScriptedDecisionClient.Answering(async (_, cancellationToken) =>
        {
            arrivals[Interlocked.Increment(ref callCount) - 1].SetResult();
            await release.WaitAsync(cancellationToken);

            return PhishingVerdict();
        });
    }

    private static Composition Judging(
        ScriptedDecisionClient client,
        bool admitted = true,
        SensitiveContentEgressGuard? egressGuard = null,
        int maximumConcurrentCalls = SpamModelStageBounds.DefaultMaximumConcurrentCalls,
        ILogger<SpamModelJudge>? logger = null)
    {
        var services = Services(admitted);
        services.AddKeyedScoped<IDecisionClient>(DecisionFunction.SpamClassification, (_, _) => client);
        services.AddSingleton(provider => Judge(
            provider,
            egressGuard ?? SensitiveContentEgressGuards.Inactive(),
            maximumConcurrentCalls,
            logger ?? NullLogger<SpamModelJudge>.Instance));

        return new Composition(services.BuildServiceProvider());
    }

    private static ServiceCollection Services(bool admitted)
    {
        var spendLedger = Substitute.For<IMailAnsweringSpendLedger>();
        spendLedger.TryAdmitRunAsync(Arg.Any<CancellationToken>()).Returns(admitted);
        var services = new ServiceCollection();
        services.AddScoped(_ => spendLedger);

        return services;
    }

    private static SpamModelJudge Judge(
        IServiceProvider provider,
        SensitiveContentEgressGuard egressGuard,
        int maximumConcurrentCalls,
        ILogger<SpamModelJudge> logger) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            egressGuard,
            new SpamModelStageBounds(maximumConcurrentCalls, SpamModelStageBounds.DefaultMaximumBodyCharacters),
            logger);

    /// <summary>The judge as the container holds it, a singleton disposed with the container its scopes come from.</summary>
    private sealed class Composition(ServiceProvider provider) : IDisposable
    {
        public bool IsActive => provider.GetRequiredService<SpamModelJudge>().IsActive;

        public Task<SpamModelJudgement> JudgeAsync(SpamModelEvidence evidence, SpamModelJob job, CancellationToken cancellationToken) =>
            provider.GetRequiredService<SpamModelJudge>().JudgeAsync(evidence, job, cancellationToken);

        public void Dispose() => provider.Dispose();
    }
}
