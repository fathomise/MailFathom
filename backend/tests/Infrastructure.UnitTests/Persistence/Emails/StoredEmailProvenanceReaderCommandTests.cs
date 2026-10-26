// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Emails;

/// <summary>
/// Covers what the provenance read asks PostgreSQL for. The command is read rather than the result, because the two
/// shapes that would make it wrong — carrying a mailbox's earlier mail back to the process, or reading every stored
/// email to answer for four — return the right provenance off a far larger read.
/// </summary>
public sealed class StoredEmailProvenanceReaderCommandTests
{
    private static readonly StoredEmailId[] Emails =
    [
        StoredEmailId.Create(new Guid("22222222-2222-2222-2222-222222222222")),
        StoredEmailId.Create(new Guid("33333333-3333-3333-3333-333333333333")),
    ];

    private static readonly MailFolderIdentity[] VisibleToAI =
    [
        new(MailAccountId.Create("work"), MailFolderAlias.Create("INBOX")),
    ];

    /// <summary>The read is narrowed to the emails a caller already holds, which the primary key serves.</summary>
    [Fact]
    public void ProvenanceOf_SeveralEmails_NarrowsTheCommandByThoseIdentities()
    {
        // Act
        var narrowing = NarrowingIn(CommandFor(Emails));

        // Assert
        Assert.Contains($"\"{nameof(StoredEmailEntity.Id)}\" = ANY", narrowing, StringComparison.Ordinal);
    }

    /// <summary>Earlier correspondence is one existence test per email, answered by PostgreSQL rather than by a set returned here.</summary>
    [Fact]
    public void ProvenanceOf_EarlierCorrespondence_IsAnExistenceTestOverTheSendersAddresses()
    {
        // Act
        var command = CommandFor(Emails);

        // Assert
        Assert.Contains("EXISTS (", command, StringComparison.Ordinal);
        Assert.Contains(nameof(StoredEmailEntity.SenderNormalizedAddress), command, StringComparison.Ordinal);
        Assert.Contains(nameof(StoredEmailEntity.ToAddresses), command, StringComparison.Ordinal);
        Assert.Contains(nameof(StoredEmailEntity.CcAddresses), command, StringComparison.Ordinal);
    }

    /// <summary>Earlier correspondence is looked for only in mail still part of the mailbox and in folders an agent may read.</summary>
    /// <remarks>
    /// Read from the existence test alone: the flag is published, so an expunged message or one in an unmapped folder
    /// deciding it would tell a caller that mail it cannot read exists.
    /// </remarks>
    [Fact]
    public void ProvenanceOf_EarlierCorrespondence_IsNarrowedToLiveMailInFoldersVisibleToAI()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
        var aliasColumn = context.Model
            .FindEntityType(typeof(MailFolderEntity))!
            .FindProperty(nameof(MailFolderEntity.Alias))!
            .GetColumnName();

        // Act
        var existenceTest = ExistenceTestIn(CommandFor(Emails));

        // Assert
        Assert.Contains($"\"{nameof(StoredEmailEntity.RemoteExpungeObservedAt)}\"", existenceTest, StringComparison.Ordinal);
        Assert.Contains($"\"{aliasColumn}\"", existenceTest, StringComparison.Ordinal);
    }

    /// <summary>Whether an email is junk is read off the role its folder was configured with, as a column the command selects.</summary>
    /// <remarks>
    /// Read from the projection alone, ahead of the first <c>FROM</c>: the earlier-correspondence test filters on the
    /// role as well, so a role anywhere in the command would still be there with the junk flag projected as a constant.
    /// </remarks>
    [Fact]
    public void ProvenanceOf_TheJunkFlag_IsProjectedFromTheFoldersConfiguredRole()
    {
        // Act
        var projection = ProjectionIn(CommandFor(Emails));

        // Assert
        Assert.Contains($"\"{nameof(MailFolderEntity.Role)}\"", projection, StringComparison.Ordinal);
    }

    /// <summary>Generates the whole command without opening a connection.</summary>
    private static string CommandFor(IReadOnlyCollection<StoredEmailId> emails)
    {
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        return StoredEmailProvenanceReader.ProvenanceOf(context, emails, VisibleToAI).ToQueryString();
    }

    /// <summary>Keeps the existence test, from its opening to the outermost <c>WHERE</c> that narrows by identity.</summary>
    private static string ExistenceTestIn(string command)
    {
        var existsIndex = command.IndexOf("EXISTS (", StringComparison.Ordinal);
        var whereIndex = command.LastIndexOf("WHERE", StringComparison.Ordinal);

        return existsIndex < 0 || whereIndex < existsIndex ? string.Empty : command[existsIndex..whereIndex];
    }

    /// <summary>Keeps what precedes the first <c>FROM</c>, which is the select list up to the first subquery it holds.</summary>
    private static string ProjectionIn(string command)
    {
        var fromIndex = command.IndexOf("FROM", StringComparison.Ordinal);

        return fromIndex < 0 ? string.Empty : command[..fromIndex];
    }

    /// <summary>Keeps what follows the outermost <c>WHERE</c>, which is where the narrowing by identity sits.</summary>
    private static string NarrowingIn(string command)
    {
        var whereIndex = command.LastIndexOf("WHERE", StringComparison.Ordinal);

        return whereIndex < 0 ? string.Empty : command[whereIndex..];
    }
}
