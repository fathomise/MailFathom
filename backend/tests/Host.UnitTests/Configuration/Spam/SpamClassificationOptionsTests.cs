// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Host.Configuration.Spam;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Spam;

/// <summary>Covers what the classification section refuses before an operator discovers it on their own mail.</summary>
public sealed class SpamClassificationOptionsTests
{
    /// <summary>The section binds with none of its keys set, which is what makes the feature opt-in rather than defaulted off.</summary>
    [Fact]
    public void FindErrors_ASectionSettingNothing_ReportsNoErrorAndClassifiesNothing()
    {
        // Arrange
        var options = new SpamClassificationOptions();

        // Act
        var errors = options.FindErrors().ToArray();

        // Assert
        Assert.Empty(errors);
        Assert.False(options.Enabled);
        Assert.Null(options.ScannedFolders);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FindErrors_AScannedFolderHoldingNoAlias_IsRefusedNamingTheEntry(string alias)
    {
        // Arrange
        var options = new SpamClassificationOptions { Enabled = true, ScannedFolders = [alias] };

        // Act
        var error = Assert.Single(options.FindErrors());

        // Assert
        Assert.Equal([nameof(SpamClassificationOptions.ScannedFolders)], error.MemberNames);
        Assert.Contains(alias, error.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>An alias this system could never have issued is a typo rather than a folder, so it is named back.</summary>
    [Fact]
    public void FindErrors_AScannedFolderCarryingAControlCharacter_IsRefusedNamingTheAlias()
    {
        // Arrange
        var alias = "junk" + (char)7 + "mail";
        var options = new SpamClassificationOptions { Enabled = true, ScannedFolders = [alias] };

        // Act
        var error = Assert.Single(options.FindErrors());

        // Assert
        Assert.Equal([nameof(SpamClassificationOptions.ScannedFolders)], error.MemberNames);
        Assert.Contains(alias, error.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>Writing no folder at all is an operator switching the work off without switching the section off.</summary>
    [Fact]
    public void FindErrors_AnExplicitlyEmptyScannedFolderList_ReportsNoError()
    {
        // Arrange
        var options = new SpamClassificationOptions { Enabled = true, ScannedFolders = [] };

        // Act, Assert
        Assert.Empty(options.FindErrors());
    }

    /// <summary>Every rule is reported at once, so an operator repairs the section rather than one key per restart.</summary>
    [Fact]
    public void FindErrors_ASectionBreakingSeveralRules_ReportsAllOfThem()
    {
        // Arrange
        var options = new SpamClassificationOptions
        {
            Enabled = false,
            ScannedFolders = ["", "   "],
            Actions = new SpamActionOptions { MoveToJunkFolder = true, JunkFolder = "junk" + (char)7 },
        };

        // Act
        var errors = options.FindErrors().ToArray();

        // Assert
        Assert.Equal(4, errors.Length);
    }
}
