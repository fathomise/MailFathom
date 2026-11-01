// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.BrowseThreadList;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Emails.Threads;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Emails.Threads;

/// <summary>
/// Generates the commands a page of conversations is read with, against the real provider and the real model, with no
/// connection opened. Grouping a conversation, ordering by an aggregate, and picking one row per group are the shapes
/// EF Core refuses by throwing while translating, so a query written the wrong way builds and fails on the first
/// request; each command here had to translate before it could be read. What the commands return is the integration
/// suite's to establish against PostgreSQL.
/// </summary>
public sealed class StoredEmailThreadListReaderCommandTests
{
    private const int PageLimit = 26;

    private static readonly Guid[] ThreadIds = [Guid.Parse("11111111-1111-1111-1111-111111111111")];

    [Fact]
    public void Page_TheFirstPage_IsOneGroupedOrderedBoundedRead()
    {
        // Act
        var command = PageCommand(continueAfter: null);

        // Assert
        Assert.Contains("GROUP BY", command, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", command, StringComparison.Ordinal);
        Assert.Contains("LIMIT", command, StringComparison.Ordinal);
    }

    /// <summary>A conversation is listed on a message that is neither spam nor in Trash, so the page carries a second read of the readable mail that decides it.</summary>
    [Fact]
    public void Page_AnyPage_DecidesWhichConversationsAreListedFromTheSpamVerdictAndTheTrashedFolders()
    {
        // Act
        var command = PageCommand(continueAfter: null);

        // Assert
        Assert.Contains("@trashedFolderIds", command, StringComparison.Ordinal);
        Assert.Contains("Verdict", command, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Page_AContinuedPage_TranslatesTheKeysetWhetherOrNotTheBoundaryCarriesADate(bool dated)
    {
        // Arrange
        var boundary = new EmailThreadListPosition(
            dated ? DateTimeOffset.Parse("2026-10-30T09:00:00Z", null) : null,
            EmailThreadId.Create(ThreadIds[0]));

        // Act
        var command = PageCommand(boundary);

        // Assert
        Assert.Contains("@boundaryId", command, StringComparison.Ordinal);
    }

    [Fact]
    public void Quoted_ThePagesConversations_PicksOneNewestMessagePerConversation()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        // Act
        var command = StoredEmailThreadListReader
            .Quoted(context.StoredEmails.AsNoTracking(), Scope(), ThreadIds, ["me@example.test"])
            .ToQueryString();

        // Assert
        Assert.Contains("@readerForms", command, StringComparison.Ordinal);
        Assert.Contains("LIMIT 1", command, StringComparison.Ordinal);
    }

    [Fact]
    public void Participants_ThePagesConversations_GroupsByConversationAndAddress()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        // Act
        var command = StoredEmailThreadListReader
            .Participants(context.StoredEmails.AsNoTracking(), Scope(), ThreadIds)
            .ToQueryString();

        // Assert
        Assert.Contains("GROUP BY", command, StringComparison.Ordinal);
        Assert.Contains("SenderNormalizedAddress", command, StringComparison.Ordinal);
    }

    [Fact]
    public void Titles_ThePagesConversations_ReadsOnlyStatesThatCarryATopic()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        // Act
        var command = StoredEmailThreadListReader
            .Titles(context.EmailThreadStates.AsNoTracking(), ThreadIds)
            .ToQueryString();

        // Assert
        Assert.Contains("\"Topic\" IS NOT NULL", command, StringComparison.Ordinal);
    }

    private static string PageCommand(EmailThreadListPosition? continueAfter)
    {
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        return StoredEmailThreadListReader
            .Page(
                context.StoredEmails.AsNoTracking(),
                Scope(),
                trashedFolderIds: [Guid.Parse("22222222-2222-2222-2222-222222222222")],
                readerForms: ["me@example.test"],
                continueAfter,
                PageLimit)
            .ToQueryString();
    }

    private static MailboxScope Scope() =>
        MailboxScope.Create([MailAccountId.Create("acct-1"), MailAccountId.Create("acct-2")], selectedFolders: null);
}
