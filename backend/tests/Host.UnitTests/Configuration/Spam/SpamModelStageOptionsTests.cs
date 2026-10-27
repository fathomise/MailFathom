// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Spam.Model;
using MailFathom.Host.Configuration.Spam;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Spam;

/// <summary>Covers the deployment's bounds on the spam model stage.</summary>
public sealed class SpamModelStageOptionsTests
{
    [Fact]
    public void ToBounds_ABlockSettingNothing_HoldsTheDefaultsWithoutAnError()
    {
        // Arrange
        var options = new SpamClassificationOptions();

        // Act
        var errors = options.FindErrors().ToArray();
        var bounds = options.ModelStage.ToBounds();

        // Assert
        Assert.Empty(errors);
        Assert.Equal(SpamModelStageBounds.Default, bounds);
    }

    [Theory]
    [InlineData(0, 8_000, nameof(SpamModelStageOptions.MaxConcurrentCalls))]
    [InlineData(65, 8_000, nameof(SpamModelStageOptions.MaxConcurrentCalls))]
    [InlineData(4, 499, nameof(SpamModelStageOptions.MaxBodyCharacters))]
    [InlineData(4, 100_001, nameof(SpamModelStageOptions.MaxBodyCharacters))]
    public void FindErrors_ABoundOutsideItsRange_IsRefusedNamingTheKey(int concurrentCalls, int bodyCharacters, string key)
    {
        // Arrange
        var options = new SpamClassificationOptions
        {
            ModelStage = new SpamModelStageOptions
            {
                MaxConcurrentCalls = concurrentCalls,
                MaxBodyCharacters = bodyCharacters,
            },
        };

        // Act
        var error = Assert.Single(options.FindErrors());

        // Assert
        Assert.StartsWith($"SpamClassification:ModelStage:{key}", error.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ToBounds_DeclaredBounds_AreTheOnesTheStageReads()
    {
        // Arrange
        var options = new SpamModelStageOptions { MaxConcurrentCalls = 2, MaxBodyCharacters = 4_000 };

        // Act
        var bounds = options.ToBounds();

        // Assert
        Assert.Equal(new SpamModelStageBounds(2, 4_000), bounds);
    }
}
