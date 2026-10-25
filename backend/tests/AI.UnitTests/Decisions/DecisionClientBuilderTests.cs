// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.Decisions;
using MailFathom.AI.UnitTests.TestDoubles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.AI.UnitTests.Decisions;

/// <summary>Covers how a decision pipeline is composed: in the order it was written, reachable through its layers, and owned by its outermost one.</summary>
public sealed class DecisionClientBuilderTests
{
    private static readonly ChatMessage[] Input = [new(ChatRole.User, "Your parcel is waiting, pay the fee here.")];

    private static readonly DecisionQuestion[] Questions = [DecisionQuestion.YesNo("spam", "Is this spam?")];

    [Fact]
    public async Task Build_SeveralMiddlewares_RunsTheFirstWrittenOutermost()
    {
        // Arrange
        var order = new List<string>();
        using var inner = ScriptedDecisionClient.AnsweringFirstOption();
        using var client = new DecisionClientBuilder(inner)
            .Use(next => new RecordingDecisionClient(next, "outer", order))
            .Use(next => new RecordingDecisionClient(next, "inner", order))
            .Build();

        // Act
        await client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["outer", "inner"], order);
        Assert.Single(inner.Calls);
    }

    [Fact]
    public void GetService_ATypeOnlyTheInnermostClientIs_IsFoundThroughEveryLayer()
    {
        // Arrange
        using var inner = ScriptedDecisionClient.AnsweringFirstOption();
        using var client = new DecisionClientBuilder(inner)
            .Use(next => new RecordingDecisionClient(next, "only", []))
            .Build();

        // Act
        var found = client.GetService<ScriptedDecisionClient>();

        // Assert
        Assert.Same(inner, found);
    }

    [Fact]
    public void Dispose_TheOutermostLayer_DisposesTheClientItWasBuiltOver()
    {
        // Arrange
        using var inner = ScriptedDecisionClient.AnsweringFirstOption();
        var client = new DecisionClientBuilder(inner)
            .Use(next => new RecordingDecisionClient(next, "only", []))
            .Build();

        // Act
        client.Dispose();

        // Assert
        Assert.True(inner.IsDisposed);
    }

    [Fact]
    public async Task AddKeyedDecisionClient_ARegisteredPipeline_IsResolvedUnderItsKeyWithItsMiddleware()
    {
        // Arrange
        var order = new List<string>();
        var services = new ServiceCollection();
        services.AddKeyedDecisionClient("spam", _ => ScriptedDecisionClient.AnsweringFirstOption())
            .Use(next => new RecordingDecisionClient(next, "registered", order));
        await using var provider = services.BuildServiceProvider();

        // Act
        var client = provider.GetRequiredKeyedService<IDecisionClient>("spam");
        await client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["registered"], order);
        Assert.Null(provider.GetService<IDecisionClient>());
    }

    private sealed class RecordingDecisionClient(IDecisionClient innerClient, string name, List<string> order)
        : DelegatingDecisionClient(innerClient)
    {
        public override Task<DecisionResponse> DecideAsync(
            IEnumerable<ChatMessage> input,
            IReadOnlyList<DecisionQuestion> questions,
            DecisionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            order.Add(name);

            return base.DecideAsync(input, questions, options, cancellationToken);
        }
    }
}
