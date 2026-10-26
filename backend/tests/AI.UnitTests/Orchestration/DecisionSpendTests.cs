// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.DecisionModels;
using MailFathom.AI.Decisions;
using MailFathom.AI.Orchestration;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.Chat;
using MailFathom.Application.Retrieval.AskMail;
using NSubstitute;
using Xunit;

namespace MailFathom.AI.UnitTests.Orchestration;

/// <summary>Covers the two ledgers a decision call is held to: the run's ceilings before it is sent, and the period's charge once it has been answered.</summary>
public sealed class DecisionSpendTests
{
    // Both namespaces publish a ChatMessage, so the framework's is qualified rather than imported.
    private static readonly Microsoft.Extensions.AI.ChatMessage[] Input =
        [new(Microsoft.Extensions.AI.ChatRole.User, "Your parcel is waiting, pay the fee here.")];

    private static readonly DecisionQuestion[] Questions = [DecisionQuestion.YesNo("spam", "Is this spam?")];

    [Fact]
    public async Task BudgetedDecideAsync_ARunPastItsCallCeiling_RefusesBeforeAnythingIsSent()
    {
        // Arrange
        using var inner = ScriptedDecisionClient.AnsweringFirstOption();
        using var client = new BudgetedDecisionClient(inner, LedgerAllowing(providerCalls: 1));

        await client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var failure = await Assert.ThrowsAsync<MailAnsweringBudgetExhaustedException>(
            () => client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailAnsweringBudgetScope.Run, failure.Scope);
        Assert.Single(inner.Calls);
    }

    [Fact]
    public async Task BudgetedDecideAsync_AnAnswerReportingTokens_ChargesThemToTheRun()
    {
        // Arrange
        using var inner = ScriptedDecisionClient.AnsweringFirstOption(inputTokens: 90, outputTokens: 10);
        var runLedger = LedgerAllowing(tokens: 100);
        using var client = new BudgetedDecisionClient(inner, runLedger);

        // Act
        await client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(100L, runLedger.Read().Tokens);
        await Assert.ThrowsAsync<MailAnsweringBudgetExhaustedException>(
            () => client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>The tokens were spent once the provider answered, so a caller that stopped waiting does not unspend them.</summary>
    [Fact]
    public async Task SpendReportingDecideAsync_AnAnswerReportingTokens_ChargesThePeriodUncancellably()
    {
        // Arrange
        using var inner = ScriptedDecisionClient.AnsweringFirstOption(inputTokens: 412, outputTokens: 0);
        var spendLedger = Substitute.For<IMailAnsweringSpendLedger>();
        using var client = new SpendReportingDecisionClient(inner, spendLedger);

        // Act
        await client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        await spendLedger.Received(1).RecordSpendAsync(new ChatTokenUsage(412, 0), CancellationToken.None);
    }

    [Fact]
    public async Task SpendReportingDecideAsync_AnAnswerReportingNoUsage_ChargesNothing()
    {
        // Arrange
        using var inner = ScriptedDecisionClient.AnsweringFirstOption();
        var spendLedger = Substitute.For<IMailAnsweringSpendLedger>();
        using var client = new SpendReportingDecisionClient(inner, spendLedger);

        // Act
        await client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        await spendLedger.DidNotReceive().RecordSpendAsync(Arg.Any<ChatTokenUsage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SpendReportingDecideAsync_AFailedCall_ChargesNothing()
    {
        // Arrange
        using var inner = ScriptedDecisionClient.Throwing(new DecisionFailedException("decider", DecisionFailure.RateLimited));
        var spendLedger = Substitute.For<IMailAnsweringSpendLedger>();
        using var client = new SpendReportingDecisionClient(inner, spendLedger);

        // Act
        await Assert.ThrowsAsync<DecisionFailedException>(
            () => client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        await spendLedger.DidNotReceive().RecordSpendAsync(Arg.Any<ChatTokenUsage>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The model ran and the provider billed it, so a reader refusing the shape does not make the tokens free.</summary>
    [Fact]
    public async Task SpendReportingDecideAsync_AnUnreadableAnswerThatReportedUsage_ChargesThePeriodAndStillFails()
    {
        // Arrange
        using var inner = ScriptedDecisionClient.Throwing(UnreadableReporting(inputTokens: 90));
        var spendLedger = Substitute.For<IMailAnsweringSpendLedger>();
        using var client = new SpendReportingDecisionClient(inner, spendLedger);

        // Act
        var failure = await Assert.ThrowsAsync<DecisionFailedException>(
            () => client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DecisionFailure.AnswerUnreadable, failure.Failure);
        await spendLedger.Received(1).RecordSpendAsync(new ChatTokenUsage(90, 0), CancellationToken.None);
    }

    [Fact]
    public async Task BudgetedDecideAsync_AnUnreadableAnswerThatReportedUsage_ChargesItToTheRun()
    {
        // Arrange
        using var inner = ScriptedDecisionClient.Throwing(UnreadableReporting(inputTokens: 90));
        var runLedger = LedgerAllowing(tokens: 1_000);
        using var client = new BudgetedDecisionClient(inner, runLedger);

        // Act
        await Assert.ThrowsAsync<DecisionFailedException>(
            () => client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(90L, runLedger.Read().Tokens);
    }

    private static DecisionFailedException UnreadableReporting(long inputTokens) =>
        new("decider", DecisionFailure.AnswerUnreadable)
        {
            Usage = new Microsoft.Extensions.AI.UsageDetails { InputTokenCount = inputTokens, OutputTokenCount = 0 },
        };

    private static MailAnsweringRunLedger LedgerAllowing(int providerCalls = 8, long tokens = 80_000) =>
        new(MailAnsweringRunBounds.Create(20_000, providerCalls, tokens));
}
