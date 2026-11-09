// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Emails.Threads;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Emails.Threads;

/// <summary>Holds what the two statements must keep saying for a message arriving during a removal to be safe.</summary>
public sealed class EmptiedEmailThreadStatementsTests
{
    /// <summary>The lock passes by a conversation a placement is still writing to, and only a survivor is a candidate.</summary>
    [Fact]
    public void LockEmptied_ReadFromTheModel_SkipsLockedConversationsAndLeavesFoldedOnesToTheirSurvivor()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        // Act
        var statement = EmptiedEmailThreadStatements.LockEmptied(context.Model);

        // Assert
        Assert.Contains("FOR UPDATE OF thread SKIP LOCKED", statement, StringComparison.Ordinal);
        Assert.Contains("\"MergedIntoEmailThreadId\" IS NULL", statement, StringComparison.Ordinal);
        Assert.Contains("NOT EXISTS", statement, StringComparison.Ordinal);
    }

    /// <summary>The re-check is what sees a message that committed before the lock was granted.</summary>
    [Fact]
    public void RemoveWithFolded_ReadFromTheModel_RechecksStoredRowsAcrossTheWholeFoldedTree()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        // Act
        var statement = EmptiedEmailThreadStatements.RemoveWithFolded(context.Model);

        // Assert
        Assert.Contains("WITH RECURSIVE folded", statement, StringComparison.Ordinal);
        Assert.Contains("JOIN \"stored_emails\" AS email ON email.\"EmailThreadId\" = folded.member", statement, StringComparison.Ordinal);
        Assert.Contains("NOT EXISTS (SELECT 1 FROM holding", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("UNION ALL", statement, StringComparison.Ordinal);
    }
}
