// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Folders;
using Xunit;

namespace MailFathom.Domain.UnitTests.Folders;

/// <summary>Covers the path a stored email's folder is read by.</summary>
public sealed class StoredEmailFolderTests
{
    [Fact]
    public void PathOf_SeveralLevels_JoinsThemOutermostFirstByTheHierarchyDelimiter()
    {
        // Arrange, Act
        var path = StoredEmailFolder.PathOf(["Projects", "2026", "Client"]);

        // Assert
        Assert.Equal(
            string.Join(LocalMailFolderName.HierarchyDelimiter, "Projects", "2026", "Client"),
            path);
    }

    [Fact]
    public void PathOf_NoLevels_IsEmpty()
    {
        // Arrange, Act
        var path = StoredEmailFolder.PathOf([]);

        // Assert
        Assert.Empty(path);
    }
}
