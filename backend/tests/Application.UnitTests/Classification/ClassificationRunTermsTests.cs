// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Classification;
using MailFathom.Application.Spam.Actions;
using MailFathom.Domain.Folders;
using Xunit;

namespace MailFathom.Application.UnitTests.Classification;

public sealed class ClassificationRunTermsTests
{
    [Fact]
    public void Create_FoldersNamedTwiceAndOutOfOrder_KeepsOneOfEachInANormalizedOrder()
    {
        // Arrange
        MailFolderAlias[] named =
        [
            MailFolderAlias.Create("INBOX"),
            MailFolderAlias.Create("ARCHIVE"),
            MailFolderAlias.Create("INBOX"),
        ];

        // Act
        var terms = ClassificationRunTerms.Create(named, [], SpamActionPosture.DryRun, rescores: false);

        // Assert
        Assert.Equal(["ARCHIVE", "INBOX"], terms.FolderAliases.Select(alias => alias.Value));
    }

    [Fact]
    public void Create_APostureOutsideTheDeclaredSet_IsRefused()
    {
        // Arrange, Act, Assert
        var failure = Assert.Throws<ArgumentOutOfRangeException>(
            () => ClassificationRunTerms.Create([], [], (SpamActionPosture)7, rescores: false));

        Assert.Equal("posture", failure.ParamName);
    }

    /// <summary>A scope narrowed to nothing is a scope of nothing, which is what the walk over it then reads.</summary>
    [Fact]
    public void Create_NoFolderNamed_NamesNoMailRatherThanAllOfIt()
    {
        // Arrange, Act
        var terms = ClassificationRunTerms.Create([], [], SpamActionPosture.Acting, rescores: true);

        // Assert
        Assert.Empty(terms.FolderAliases);
        Assert.Equal(SpamActionPosture.Acting, terms.Posture);
        Assert.True(terms.Rescores);
    }

    /// <summary>A folder only one function reaches is walked for that half, so the walk is the union and each list stays its own.</summary>
    [Fact]
    public void Create_AFolderOnlyOneFunctionCovers_IsWalkedAndKeptToThatFunction()
    {
        // Arrange
        var inbox = MailFolderAlias.Create("INBOX");
        var archive = MailFolderAlias.Create("ARCHIVE");

        // Act
        var terms = ClassificationRunTerms.Create([inbox], [inbox, archive], SpamActionPosture.DryRun, rescores: false);

        // Assert
        Assert.Equal([archive, inbox], terms.FolderAliases);
        Assert.Equal([inbox], terms.SpamFolderAliases);
        Assert.Equal([archive, inbox], terms.CategoryFolderAliases);
        Assert.True(terms.CoversSpam);
        Assert.True(terms.CoversCategories);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Create_OneOwnCategoryBesideSpamOrRescoringOrActing_IsRefused(bool coversSpam, bool rescores, bool acts)
    {
        // Arrange
        var inbox = MailFolderAlias.Create("INBOX");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => ClassificationRunTerms.Create(
            coversSpam ? [inbox] : [],
            [inbox],
            acts ? SpamActionPosture.Acting : SpamActionPosture.DryRun,
            rescores,
            ownCategory: "Heron"));
    }
}
