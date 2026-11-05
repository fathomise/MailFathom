// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Rules.Sets;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules.Sets;

/// <summary>How a surface is read back from the name an archive row stores.</summary>
public sealed class MailRuleChangeSurfaceTests
{
    [Fact]
    public void TryParse_TheStoredNameOfEveryDeclaredSurface_ReturnsThatSurface()
    {
        // Act
        var parsed = MailRuleChangeSurface.All
            .Select(surface => MailRuleChangeSurface.TryParse(surface.ToString(), out var read) ? read : default)
            .ToArray();

        // Assert
        Assert.Equal(MailRuleChangeSurface.All, parsed);
    }

    /// <summary>An older replica reads a row a newer one wrote, so a surface it does not know is unknown rather than new.</summary>
    [Theory]
    [InlineData("Client")]
    [InlineData("administration")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_ANameThisBuildDoesNotDeclare_AnswersTheUnspecifiedDefault(string? name)
    {
        // Act
        var parsed = MailRuleChangeSurface.TryParse(name, out var surface);

        // Assert
        Assert.False(parsed);
        Assert.False(surface.IsSpecified);
        Assert.Equal("(unspecified)", surface.ToString());
    }
}
