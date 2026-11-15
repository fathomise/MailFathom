// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Folders;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Folders;

/// <summary>Covers which folders of an account are read as holding mail the person put away.</summary>
public sealed class StoredPutAwayFoldersTests
{
    private static readonly Guid Inbox = Guid.Parse("0199c1a2-6f3e-7a41-8b2d-5e9f0c4d7c01");

    private static readonly Guid Projects = Guid.Parse("0199c1a2-6f3e-7a41-8b2d-5e9f0c4d7c02");

    private static readonly Guid Trash = Guid.Parse("0199c1a2-6f3e-7a41-8b2d-5e9f0c4d7c03");

    private static readonly Guid Deleted = Guid.Parse("0199c1a2-6f3e-7a41-8b2d-5e9f0c4d7c04");

    private static readonly Guid Archive = Guid.Parse("0199c1a2-6f3e-7a41-8b2d-5e9f0c4d7c05");

    private static readonly Guid ArchivedYear = Guid.Parse("0199c1a2-6f3e-7a41-8b2d-5e9f0c4d7c06");

    private static readonly Guid ArchivedQuarter = Guid.Parse("0199c1a2-6f3e-7a41-8b2d-5e9f0c4d7c07");

    /// <summary>Trash and Archive each put away their whole tree, and a folder under any other role stays out.</summary>
    [Fact]
    public void Within_FoldersBeneathTrashOrArchive_ArePutAwayBesideTheTwoThemselves()
    {
        // Arrange
        StoredTrashedFolders.StoredFolderRole[] folders =
        [
            new(Inbox, null, MailFolderSpecialUse.Inbox),
            new(Projects, Inbox, null),
            new(Trash, null, MailFolderSpecialUse.Trash),
            new(Deleted, Trash, null),
            new(Archive, null, MailFolderSpecialUse.Archive),
            new(ArchivedYear, Archive, null),
            new(ArchivedQuarter, ArchivedYear, null),
        ];

        // Act
        var putAway = StoredPutAwayFolders.Within(folders);

        // Assert
        Assert.Equal([Trash, Deleted, Archive, ArchivedYear, ArchivedQuarter], putAway.Order());
    }

    /// <summary>An account with neither folder has put nothing away, so no folder is named.</summary>
    [Fact]
    public void Within_AnAccountWithNeitherTrashNorArchive_NamesNoFolder()
    {
        // Arrange
        StoredTrashedFolders.StoredFolderRole[] folders =
        [
            new(Inbox, null, MailFolderSpecialUse.Inbox),
            new(Projects, Inbox, null),
        ];

        // Act
        var putAway = StoredPutAwayFolders.Within(folders);

        // Assert
        Assert.Empty(putAway);
    }
}
