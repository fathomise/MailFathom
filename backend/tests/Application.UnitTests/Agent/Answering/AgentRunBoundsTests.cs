// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Agent.Answering;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Retrieval.AskMail;
using Xunit;

namespace MailFathom.Application.UnitTests.Agent.Answering;

/// <summary>Covers which ceilings a run starts under in each mode, and the rule that deep never reads less than fast.</summary>
public sealed class AgentRunBoundsTests
{
    private static readonly MailAnsweringRunBounds Fast = MailAnsweringRunBounds.Create(20_000, 8, 160_000);
    private static readonly MailAnsweringRunBounds Deep = MailAnsweringRunBounds.Create(100_000, 24, 1_000_000);

    /// <summary>A deep question starts under the deep ceilings, and a fast or an automatic one under the fast ones.</summary>
    [Theory]
    [InlineData(AgentAnswerMode.Auto, false)]
    [InlineData(AgentAnswerMode.Fast, false)]
    [InlineData(AgentAnswerMode.Deep, true)]
    public void StartingFor_EachMode_IsTheCeilingsThatModeStartsUnder(AgentAnswerMode mode, bool deep)
    {
        // Arrange
        var bounds = new AgentRunBounds(Fast, Deep);

        // Act
        var starting = bounds.StartingFor(mode);

        // Assert
        Assert.Equal(deep ? Deep : Fast, starting);
    }

    /// <summary>Each deep ceiling below its fast one is refused, because the mode that reads further would read less.</summary>
    [Theory]
    [InlineData(10_000, 24, 1_000_000)]
    [InlineData(100_000, 4, 1_000_000)]
    [InlineData(100_000, 24, 80_000)]
    public void Constructor_ADeepCeilingBelowItsFastOne_IsRefused(int retrievedCharacters, int providerCalls, long tokens)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(
            () => new AgentRunBounds(Fast, MailAnsweringRunBounds.Create(retrievedCharacters, providerCalls, tokens)));
    }

    /// <summary>The defaults a deployment stating nothing receives hold together: deep reaches further than fast in every ceiling.</summary>
    [Fact]
    public void Default_ADeploymentStatingNothing_HoldsDeepAboveFastInEveryCeiling()
    {
        // Act
        var bounds = AgentRunBounds.Default;

        // Assert
        Assert.Equal(MailAnsweringRunBounds.Default, bounds.Fast);
        Assert.True(bounds.Deep.MaximumRetrievedCharacters > bounds.Fast.MaximumRetrievedCharacters);
        Assert.True(bounds.Deep.MaximumProviderCalls > bounds.Fast.MaximumProviderCalls);
        Assert.True(bounds.Deep.MaximumTokens > bounds.Fast.MaximumTokens);
        Assert.True(bounds.Deep.MaximumTokens <= MailAnsweringPeriodBounds.Default.MaximumTokens);
    }
}
