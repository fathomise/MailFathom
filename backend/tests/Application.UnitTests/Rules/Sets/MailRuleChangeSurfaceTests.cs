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
    public void Restore_TheStoredNameOfADeclaredSurface_ReturnsThatSurface()
    {
        // Act
        var restored = MailRuleChangeSurface.Restore(MailRuleChangeSurface.Administration.Name);

        // Assert
        Assert.Equal(MailRuleChangeSurface.Administration, restored);
        Assert.True(restored.IsDeclared);
    }

    /// <summary>An older replica reads a row a newer one wrote, so a surface it does not know is kept rather than refused.</summary>
    [Fact]
    public void Restore_ANameThisBuildDoesNotDeclare_KeepsItAsAnUndeclaredSurface()
    {
        // Act
        var restored = MailRuleChangeSurface.Restore("Client");

        // Assert
        Assert.Equal("Client", restored.Name);
        Assert.False(restored.IsDeclared);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Restore_ABlankName_IsRefused(string name)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => MailRuleChangeSurface.Restore(name));
    }
}
