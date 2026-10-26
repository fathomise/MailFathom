// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Spam.Model;
using Xunit;

namespace MailFathom.Application.UnitTests.Spam.Model;

public sealed class SpamModelStageSettingsTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.1)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Consulted_ThresholdThatIsNoProbabilityAboveZero_IsRefused(double threshold)
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => SpamModelStageSettings.Consulted(false, threshold));
    }

    [Fact]
    public void Consulted_ThresholdOfOne_IsKept()
    {
        // Arrange, Act
        var settings = SpamModelStageSettings.Consulted(true, 1.0);

        // Assert
        Assert.True(settings.IsConsulted);
        Assert.True(settings.NamesSubcategoriesOfDeterministicSpam);
        Assert.Equal(1.0, settings.Threshold);
    }

    [Fact]
    public void NotConsulted_IsNeitherConsultedNorNaming()
    {
        // Arrange, Act
        var settings = SpamModelStageSettings.NotConsulted;

        // Assert
        Assert.False(settings.IsConsulted);
        Assert.False(settings.NamesSubcategoriesOfDeterministicSpam);
    }
}
