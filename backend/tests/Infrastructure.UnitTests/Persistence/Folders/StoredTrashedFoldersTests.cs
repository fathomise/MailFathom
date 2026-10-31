// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Folders;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Folders;

/// <summary>Covers which folders of an account are read as sitting in its Trash.</summary>
public sealed class StoredTrashedFoldersTests
{
    private static readonly Guid Inbox = Guid.Parse("0199c1a2-6f3e-7a41-8b2d-5e9f0c4d7b01");

    private static readonly Guid Trash = Guid.Parse("0199c1a2-6f3e-7a41-8b2d-5e9f0c4d7b02");

    private static readonly Guid Deleted = Guid.Parse("0199c1a2-6f3e-7a41-8b2d-5e9f0c4d7b03");

    private static readonly Guid DeletedChild = Guid.Parse("0199c1a2-6f3e-7a41-8b2d-5e9f0c4d7b04");

    private static readonly Guid Projects = Guid.Parse("0199c1a2-6f3e-7a41-8b2d-5e9f0c4d7b05");

    /// <summary>A deleted folder is moved beneath Trash, so its mail and its subfolders' mail count as deleted too.</summary>
    [Fact]
    public void Within_FoldersBeneathTrash_AreInTrashBesideTrashItself()
    {
        // Arrange
        StoredTrashedFolders.StoredFolderRole[] folders =
        [
            new(Inbox, null, MailFolderSpecialUse.Inbox),
            new(Trash, null, MailFolderSpecialUse.Trash),
            new(Deleted, Trash, null),
            new(DeletedChild, Deleted, null),
            new(Projects, Inbox, null),
        ];

        // Act
        var trashed = StoredTrashedFolders.Within(folders);

        // Assert
        Assert.Equal([Trash, Deleted, DeletedChild], trashed.Order());
    }

    /// <summary>A chain broken by an erased parent or corrupted into a loop ends outside Trash rather than spinning.</summary>
    [Fact]
    public void Within_AChainThatEndsOrLoopsWithoutTrash_IsNotInTrash()
    {
        // Arrange
        StoredTrashedFolders.StoredFolderRole[] folders =
        [
            new(Deleted, Trash, null),
            new(Projects, DeletedChild, null),
            new(DeletedChild, Projects, null),
        ];

        // Act
        var trashed = StoredTrashedFolders.Within(folders);

        // Assert
        Assert.Empty(trashed);
    }
}
