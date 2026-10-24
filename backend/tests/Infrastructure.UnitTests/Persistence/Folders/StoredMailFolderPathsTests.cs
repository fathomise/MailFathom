// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Folders;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Folders;

/// <summary>Covers how a folder's place in its account's hierarchy is read from the parent chain its rows describe.</summary>
public sealed class StoredMailFolderPathsTests
{
    private static readonly Guid Projects = Guid.Parse("0199c1a2-6f3e-7a41-8b2d-5e9f0c4d7a01");

    private static readonly Guid Year = Guid.Parse("0199c1a2-6f3e-7a41-8b2d-5e9f0c4d7a02");

    private static readonly Guid Client = Guid.Parse("0199c1a2-6f3e-7a41-8b2d-5e9f0c4d7a03");

    [Fact]
    public void Compose_ANestedFolder_NamesEveryLevelFromTheTopDown()
    {
        // Arrange
        StoredMailFolderNode[] nodes =
        [
            new(Client, Year, "Client"),
            new(Projects, null, "Projects"),
            new(Year, Projects, "2026"),
        ];

        // Act
        var paths = StoredMailFolderPaths.Compose(nodes);

        // Assert
        Assert.Equal(["Projects", "2026", "Client"], paths[Client]);
        Assert.Equal(["Projects"], paths[Projects]);
    }

    /// <summary>A folder whose parent was erased reads as the top of what remains rather than disappearing.</summary>
    [Fact]
    public void Compose_AParentTheRowsDoNotCarry_EndsThePathAtTheFolder()
    {
        // Arrange
        StoredMailFolderNode[] nodes = [new(Year, Projects, "2026")];

        // Act
        var paths = StoredMailFolderPaths.Compose(nodes);

        // Assert
        Assert.Equal(["2026"], paths[Year]);
    }

    [Fact]
    public void Compose_AChainThatLoops_EndsAtTheFirstFolderItRevisits()
    {
        // Arrange
        StoredMailFolderNode[] nodes = [new(Projects, Year, "Projects"), new(Year, Projects, "2026")];

        // Act
        var paths = StoredMailFolderPaths.Compose(nodes);

        // Assert
        Assert.Equal(["2026", "Projects"], paths[Projects]);
    }

    [Fact]
    public void Describe_AFolderThePathsCarry_NamesItByItsIdentityAndItsJoinedPath()
    {
        // Arrange
        var paths = new Dictionary<Guid, IReadOnlyList<string>> { [Year] = ["Projects", "2026"] };

        // Act
        var folder = StoredMailFolderPaths.Describe(Year, "PROJECTS-2026", paths);

        // Assert
        Assert.Equal(
            new StoredEmailFolder(MailFolderId.Create(Year), "Projects/2026", MailFolderAlias.Create("PROJECTS-2026")),
            folder);
    }

    [Fact]
    public void Describe_AFolderNoAliasFeedsAndThePathsDoNotCarry_HasNoAliasAndAnEmptyPath()
    {
        // Arrange, Act
        var folder = StoredMailFolderPaths.Describe(Client, alias: null, new Dictionary<Guid, IReadOnlyList<string>>());

        // Assert
        Assert.Equal(new StoredEmailFolder(MailFolderId.Create(Client), string.Empty, null), folder);
    }
}
