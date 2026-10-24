// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Folders;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Folders;

/// <summary>Covers how a mirrored account's folder rows follow the shape of the source folders they stand for.</summary>
public sealed class MailFolderProjectionTests
{
    private const string Account = "work";

    [Fact]
    public void Project_AFolderNestedOnTheServer_HangsItsRowUnderARowForItsRemoteParent()
    {
        // Arrange
        var folders = new List<MailFolderEntity>();

        // Act
        var projected = Project(folders, Binding("PROJECTS-2026", "Projects/2026"));

        // Assert
        var parent = Assert.Single(folders, folder => folder.Id != projected.Id);
        Assert.Equal(("Projects", null, (Guid?)null), (parent.Name, parent.Alias, parent.ParentId));
        Assert.Equal(("2026", "PROJECTS-2026", (Guid?)parent.Id), (projected.Name, projected.Alias, projected.ParentId));
    }

    [Fact]
    public void Project_AParentAnotherAliasNames_HangsTheRowUnderThatAliasesRow()
    {
        // Arrange
        var folders = new List<MailFolderEntity>();
        var aliases = new Dictionary<string, string> { ["Projects"] = "PROJECTS" };
        var parent = Project(folders, Binding("PROJECTS", "Projects"), aliases);

        // Act
        var child = Project(folders, Binding("PROJECTS-2026", "Projects/2026"), aliases);

        // Assert
        Assert.Equal(parent.Id, child.ParentId);
        Assert.Equal(2, folders.Count);
    }

    [Fact]
    public void Project_AFolderAlreadyInShape_AddsNoRowAndKeepsItsIdentity()
    {
        // Arrange
        var folders = new List<MailFolderEntity>();
        var first = Project(folders, Binding("PROJECTS-2026", "Projects/2026"));

        // Act
        var second = Project(folders, Binding("PROJECTS-2026", "Projects/2026"));

        // Assert
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(2, folders.Count);
    }

    /// <summary>The identity is what every reference holds, so a rename on the server moves the name and never the row.</summary>
    [Fact]
    public void Project_ASourceFolderRenamedOnTheServer_KeepsTheRowsIdentityUnderTheNewName()
    {
        // Arrange
        var folders = new List<MailFolderEntity>();
        var before = Project(folders, Binding("ARCHIVE", "Archive"));

        // Act
        var after = Project(folders, Binding("ARCHIVE", "Archiwum"));

        // Assert
        Assert.Equal(before.Id, after.Id);
        Assert.Equal("Archiwum", after.Name);
    }

    [Fact]
    public void Project_ARoleAFolderNoAliasNamesPlays_TakesItFromThatFolder()
    {
        // Arrange
        var leftover = Row("Kosz", alias: null, MailFolderSpecialUse.Trash);
        var folders = new List<MailFolderEntity> { leftover };

        // Act
        var projected = Project(folders, Binding("TRASH", "Trash"), role: MailFolderSpecialUse.Trash);

        // Assert
        Assert.Equal((MailFolderSpecialUse?)MailFolderSpecialUse.Trash, projected.Role);
        Assert.Null(leftover.Role);
    }

    [Fact]
    public void Project_ARoleAnotherAliasesFolderPlays_LeavesItWithThatFolder()
    {
        // Arrange
        var holder = Row("Trash", "TRASH", MailFolderSpecialUse.Trash);
        var folders = new List<MailFolderEntity> { holder };

        // Act
        var projected = Project(folders, Binding("BIN", "Bin"), role: MailFolderSpecialUse.Trash);

        // Assert
        Assert.Null(projected.Role);
        Assert.Equal((MailFolderSpecialUse?)MailFolderSpecialUse.Trash, holder.Role);
    }

    [Fact]
    public void Project_ASiblingAnotherAliasNamesCarryingTheSameName_SetsTheFolderApartByItsAlias()
    {
        // Arrange
        var folders = new List<MailFolderEntity> { Row("Archive", "ARCHIVE", role: null) };

        // Act
        var projected = Project(folders, Binding("OLD-ARCHIVE", "archive"));

        // Assert
        Assert.Equal("archive (OLD-ARCHIVE)", projected.Name);
    }

    [Fact]
    public void Declare_AnAliasNoRowCarries_GivesItATopLevelRowPlayingTheDeclaredRole()
    {
        // Arrange
        var folders = new List<MailFolderEntity>();

        // Act
        var declared = MailFolderProjection.Declare(folders, Account, "JUNK", "Spam", MailFolderSpecialUse.Junk, Guid.NewGuid);

        // Assert
        Assert.NotNull(declared);
        Assert.Equal(("Spam", "JUNK", (Guid?)null, (MailFolderSpecialUse?)MailFolderSpecialUse.Junk), (declared.Name, declared.Alias, declared.ParentId, declared.Role));
        Assert.Same(declared, Assert.Single(folders));
    }

    [Fact]
    public void Declare_AnAliasARowAlreadyCarries_CreatesNothing()
    {
        // Arrange
        var folders = new List<MailFolderEntity> { Row("Spam", "JUNK", MailFolderSpecialUse.Junk) };

        // Act
        var declared = MailFolderProjection.Declare(folders, Account, "JUNK", "Junk", MailFolderSpecialUse.Junk, Guid.NewGuid);

        // Assert
        Assert.Null(declared);
        Assert.Single(folders);
    }

    private static MailFolderEntity Project(
        List<MailFolderEntity> folders,
        MailFolderBindingEntity binding,
        IReadOnlyDictionary<string, string>? aliasesByRemotePath = null,
        MailFolderSpecialUse? role = null) =>
        MailFolderProjection.Project(
            folders,
            aliasesByRemotePath ?? new Dictionary<string, string>(),
            binding,
            role,
            Guid.NewGuid);

    private static MailFolderBindingEntity Binding(string alias, string remotePath) => new()
    {
        MailboxAccountId = Account,
        Alias = alias,
        RemotePath = remotePath,
        HierarchyDelimiter = "/",
        MailboxAccount = new MailboxAccountEntity { Id = Account },
    };

    private static MailFolderEntity Row(string name, string? alias, MailFolderSpecialUse? role) => new()
    {
        Id = Guid.NewGuid(),
        MailboxAccountId = Account,
        Name = name,
        NameKey = LocalMailFolderName.Create(name).ComparisonKey,
        Alias = alias,
        Role = role,
    };
}
