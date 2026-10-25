// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Spam;
using MailFathom.Domain.Folders;
using Xunit;

namespace MailFathom.Application.UnitTests.Spam;

public sealed class SpamClassificationSettingsTests
{
    private static readonly string[] RepeatedAliases = ["JUNK", "INBOX", "JUNK"];

    /// <summary>A deployment that configured nothing runs every switch off and classifies no folder.</summary>
    [Fact]
    public void Disabled_TheSettingsAConfiguredNothingProduces_ClassifiesNothing()
    {
        // Arrange, Act
        var settings = SpamClassificationSettings.Disabled;

        // Assert
        Assert.False(settings.IsEnabled);
        Assert.Empty(settings.ScannedFolderAliases);
        Assert.False(settings.Covers(MailFolderAlias.Create("INBOX")));
    }

    /// <summary>Two configured entries naming one folder are one folder, whatever order they arrived in.</summary>
    [Fact]
    public void Create_AliasesRepeatedAndOutOfOrder_AreOneNormalizedScope()
    {
        // Arrange
        var aliases = RepeatedAliases.Select(MailFolderAlias.Create);

        // Act
        var settings = SpamClassificationSettings.Create(isEnabled: true, aliases);

        // Assert
        Assert.Equal(["INBOX", "JUNK"], settings.ScannedFolderAliases.Select(alias => alias.Value));
    }

    [Fact]
    public void Covers_AnAliasOutsideTheConfiguredScope_IsNotClassified()
    {
        // Arrange
        var settings = SpamClassificationSettings.Create(
            isEnabled: true,
            [MailFolderAlias.Create("INBOX")]);

        // Act, Assert
        Assert.True(settings.Covers(MailFolderAlias.Create("INBOX")));
        Assert.False(settings.Covers(MailFolderAlias.Create("ARCHIVE")));
    }

    [Fact]
    public void Create_NoAliasSequence_IsRefused()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentNullException>(() => SpamClassificationSettings.Create(
            isEnabled: true,
            scannedFolderAliases: null!));
    }
}
