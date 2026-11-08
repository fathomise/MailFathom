// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Evaluations.Providers;
using Xunit;

namespace MailFathom.Evaluations.UnitTests.Providers;

/// <summary>Proves an unanswered call is named by its failure's type and status and never by what the provider wrote.</summary>
public sealed class UnansweredProviderCallTests
{
    [Fact]
    public void Describe_ARefusalCarryingAStatus_NamesTheTypeAndTheStatusAlone()
    {
        // Arrange
        var refusal = FakeProviderResponse.Refusal(429, ("Retry-After", "30"));

        // Act
        var described = UnansweredProviderCall.Describe(refusal);

        // Assert
        Assert.Equal("ClientResultException 429", described);
    }

    [Fact]
    public void Describe_AFailureCarryingNoStatus_NamesTheTypeAlone()
    {
        // Act
        var described = UnansweredProviderCall.Describe(new TaskCanceledException("planted-model at planted-host timed out"));

        // Assert
        Assert.Equal("TaskCanceledException", described);
    }
}
