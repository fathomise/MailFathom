// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Folders;
using Xunit;

namespace MailFathom.Domain.UnitTests.Folders;

/// <summary>Covers the one written form a folder is named in wherever a caller names one.</summary>
public sealed class MailFolderReferenceTests
{
    [Theory]
    [InlineData("inbox", "INBOX")]
    [InlineData("  Archive/2026  ", "ARCHIVE/2026")]
    public void Create_TextWithoutTheRoleScheme_NamesTheAliasItSpells(string written, string expected)
    {
        // Arrange, Act
        var reference = MailFolderReference.Create(written);

        // Assert
        Assert.Equal(MailFolderAlias.Create(expected), reference.Alias);
        Assert.Null(reference.Role);
        Assert.Equal(expected, reference.ToString());
    }

    [Theory]
    [InlineData("role:Junk")]
    [InlineData("ROLE:junk")]
    [InlineData("  role: Junk  ")]
    public void Create_TextBehindTheRoleScheme_NamesTheRoleItSpells(string written)
    {
        // Arrange, Act
        var reference = MailFolderReference.Create(written);

        // Assert
        Assert.Equal(MailFolderSpecialUse.Junk, reference.Role);
        Assert.Null(reference.Alias);
        Assert.Equal("role:Junk", reference.ToString());
    }

    /// <summary>Without the scheme the two would collide, and a deployment renaming its alias would silently start meaning the role.</summary>
    [Fact]
    public void Create_AnAliasSpelledLikeARole_NamesTheAliasRatherThanTheRole()
    {
        // Arrange, Act
        var reference = MailFolderReference.Create("Junk");

        // Assert
        Assert.Equal(MailFolderAlias.Create("JUNK"), reference.Alias);
        Assert.Null(reference.Role);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("in\tbox")]
    [InlineData("role:")]
    [InlineData("role:   ")]
    [InlineData("role:NotARole")]
    [InlineData("role:4")]
    [InlineData("role:Outbox")]
    public void Create_TextNamingNoFolder_ThrowsArgumentException(string written)
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentException>(() => MailFolderReference.Create(written));
    }

    [Fact]
    public void TryCreate_TextNamingNoFolder_ReportsTheUnspecifiedDefault()
    {
        // Arrange, Act
        var read = MailFolderReference.TryCreate("role:NotARole", out var reference);

        // Assert
        Assert.False(read);
        Assert.False(reference.IsSpecified);
    }

    [Fact]
    public void ToRole_RoleThatDoesNotExist_ThrowsArgumentOutOfRangeException()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => MailFolderReference.ToRole((MailFolderSpecialUse)99));
    }

    /// <summary>The struct default is reachable and names nothing, which every consumer refuses rather than reads.</summary>
    [Fact]
    public void IsSpecified_TheStructDefault_ReportsThatItNamesNoFolder()
    {
        // Arrange, Act
        var reference = default(MailFolderReference);

        // Assert
        Assert.False(reference.IsSpecified);
        Assert.Equal("(unspecified)", reference.ToString());
    }

    [Fact]
    public void ToAlias_TheSameAliasTwice_ProducesOneValue()
    {
        // Arrange, Act
        var first = MailFolderReference.ToAlias(MailFolderAlias.Create("inbox"));
        var second = MailFolderReference.Create("INBOX");

        // Assert
        Assert.Equal(first, second);
        Assert.True(first.IsSpecified);
    }

    [Fact]
    public void CreateNamedByCaller_AFolderIdentifier_NamesThatFolder()
    {
        // Arrange
        var folder = MailFolderId.Create(Guid.Parse("0199c1a2-6f3e-7a41-8b2d-5e9f0c4d7a10"));

        // Act
        var reference = MailFolderReference.CreateNamedByCaller($" {folder} ");

        // Assert
        Assert.Equal(folder, reference.Folder);
        Assert.Null(reference.Alias);
        Assert.Null(reference.Role);
        Assert.Equal(folder.ToString(), reference.ToString());
    }

    [Fact]
    public void CreateNamedByCaller_TextBehindTheRoleScheme_NamesTheRoleItSpells()
    {
        // Arrange, Act
        var reference = MailFolderReference.CreateNamedByCaller("role:junk");

        // Assert
        Assert.Equal(MailFolderSpecialUse.Junk, reference.Role);
        Assert.Null(reference.Folder);
    }

    /// <summary>An alias is configuration's name for a folder, and a surface accepting one would give every folder two names.</summary>
    [Theory]
    [InlineData("INBOX")]
    [InlineData("Archive/2026")]
    [InlineData("role:Archive,Drafts")]
    [InlineData("role:Outbox")]
    [InlineData("")]
    public void TryCreateNamedByCaller_TextNamingNoIdentifierAndNoRole_IsRefused(string written)
    {
        // Arrange, Act
        var named = MailFolderReference.TryCreateNamedByCaller(written, out var reference);

        // Assert
        Assert.False(named);
        Assert.False(reference.IsSpecified);
    }

    [Fact]
    public void CreateNamedByCaller_AnAlias_IsRefusedNamingWhatItAccepts()
    {
        // Arrange, Act
        var refusal = Assert.Throws<ArgumentException>(() => MailFolderReference.CreateNamedByCaller("INBOX"));

        // Assert
        Assert.Contains("folder identifier", refusal.Message, StringComparison.Ordinal);
    }
}
