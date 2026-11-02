// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.BrowseThreadList;
using MailFathom.Application.Emails.Enrichment;
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

    /// <summary>
    /// The first page of the grouped walk is one grouped, bounded read that decides listing on spam and Trash and orders
    /// the conversations by their newest passing arrival, undated last, then by that message's identifier — newest
    /// first or oldest first as the walk asked, which is the timeline's own order.
    /// </summary>
    [Theory]
    [InlineData(
        EmailTimelineDirection.NewestFirst,
        "ORDER BY max(s.\"ReceivedAt\") IS NULL, max(s.\"ReceivedAt\") DESC, (",
        "LIMIT 1) DESC\nLIMIT @p")]
    [InlineData(
        EmailTimelineDirection.OldestFirst,
        "ORDER BY max(s.\"ReceivedAt\") IS NULL DESC, max(s.\"ReceivedAt\"), (",
        "LIMIT 1)\nLIMIT @p")]
    public void GroupedPage_TheFirstPageInEitherDirection_OrdersByTheNewestPassingArrivalThenItsMessage(
        EmailTimelineDirection direction,
        string arrivalOrder,
        string messageOrderAndLimit)
    {
        // Act
        var command = GroupedPageCommand(Filter(direction, mark: null), continueAfter: null);

        // Assert
        Assert.Contains("GROUP BY", command, StringComparison.Ordinal);
        Assert.Contains("@trashedFolderIds", command, StringComparison.Ordinal);
        Assert.Contains("Verdict", command, StringComparison.Ordinal);
        Assert.DoesNotContain("HAVING", command, StringComparison.Ordinal);
        Assert.Contains(arrivalOrder, OuterOrder(command), StringComparison.Ordinal);
        Assert.EndsWith(messageOrderAndLimit, OuterOrder(command), StringComparison.Ordinal);
    }

    /// <summary>
    /// A continued page keeps the conversations beyond the boundary in the walk's direction: past its arrival, or at
    /// that arrival past its message, with an undated boundary reaching only the undated conversations when walking
    /// newest first and every dated one besides when walking oldest first.
    /// </summary>
    [Theory]
    [InlineData(
        EmailTimelineDirection.NewestFirst,
        true,
        "HAVING max(s.\"ReceivedAt\") IS NULL OR max(s.\"ReceivedAt\") < @arrival OR (max(s.\"ReceivedAt\") = @arrival AND (",
        "LIMIT 1) < @")]
    [InlineData(
        EmailTimelineDirection.NewestFirst,
        false,
        "HAVING max(s.\"ReceivedAt\") IS NULL AND (",
        "LIMIT 1) < @")]
    [InlineData(
        EmailTimelineDirection.OldestFirst,
        true,
        "HAVING max(s.\"ReceivedAt\") IS NOT NULL AND (max(s.\"ReceivedAt\") > @arrival OR (max(s.\"ReceivedAt\") = @arrival AND (",
        "LIMIT 1) > @")]
    [InlineData(
        EmailTimelineDirection.OldestFirst,
        false,
        "HAVING max(s.\"ReceivedAt\") IS NOT NULL OR (",
        "LIMIT 1) > @")]
    public void GroupedPage_AContinuedPage_KeepsOnlyTheConversationsBeyondTheBoundaryInTheWalksDirection(
        EmailTimelineDirection direction,
        bool datedBoundary,
        string arrivalComparison,
        string messageComparison)
    {
        // Arrange
        var boundary = new EmailTimelinePosition(
            datedBoundary ? DateTimeOffset.Parse("2026-10-30T09:00:00Z", null) : null,
            StoredEmailId.Create(Guid.Parse("33333333-3333-3333-3333-333333333333")));

        // Act
        var command = GroupedPageCommand(Filter(direction, mark: null), boundary);

        // Assert
        var keyset = command[command.IndexOf("HAVING", StringComparison.Ordinal)..command.LastIndexOf("ORDER BY max(", StringComparison.Ordinal)];

        Assert.StartsWith(arrivalComparison, keyset, StringComparison.Ordinal);
        Assert.Contains(messageComparison, keyset, StringComparison.Ordinal);
        Assert.Equal(datedBoundary, keyset.Contains("@arrival", StringComparison.Ordinal));
    }

    /// <summary>The list's filters reach the grouped walk through the shared predicate, an AI filter's mark criteria among them.</summary>
    [Fact]
    public void GroupedPage_UnderAMarkCriterionAndTheUnreadFilter_ComposesBothBeforeGrouping()
    {
        // Act
        var command = GroupedPageCommand(
            Filter(
                EmailTimelineDirection.NewestFirst,
                EmailMarkSelection.Create(EmailEnrichmentAspect.Commitment, dueOnOrAfter: null, dueBefore: null),
                isRemotelySeen: false),
            continueAfter: null);

        // Assert
        Assert.Contains("@aspect", command, StringComparison.Ordinal);
        Assert.Contains("@isRemotelySeen", command, StringComparison.Ordinal);
    }

    [Fact]
    public void Counted_TheChosenConversations_CountsEachWholeInOneGroupedRead()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        // Act
        var command = StoredEmailThreadListReader
            .Counted(context.StoredEmails.AsNoTracking(), Scope(), ThreadIds, ["me@example.test"])
            .ToQueryString();

        // Assert
        Assert.Contains("GROUP BY", command, StringComparison.Ordinal);
        Assert.Contains("@readerForms", command, StringComparison.Ordinal);
        Assert.DoesNotContain("LIMIT", command, StringComparison.Ordinal);
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

    private static string GroupedPageCommand(EmailTimelineFilter filter, EmailTimelinePosition? continueAfter)
    {
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        return StoredEmailThreadListReader
            .GroupedPage(
                context.StoredEmails.AsNoTracking(),
                filter,
                trashedFolderIds: [Guid.Parse("22222222-2222-2222-2222-222222222222")],
                continueAfter,
                PageLimit)
            .ToQueryString();
    }

    private static string OuterOrder(string command) =>
        command[command.LastIndexOf("ORDER BY max(", StringComparison.Ordinal)..].ReplaceLineEndings("\n").TrimEnd();

    private static EmailTimelineFilter Filter(
        EmailTimelineDirection direction,
        EmailMarkSelection? mark,
        bool? isRemotelySeen = null) => EmailTimelineFilter.Create(
        Scope(),
        senderAddress: null,
        recipientAddress: null,
        subjectFragment: null,
        receivedOnOrAfter: null,
        receivedBefore: null,
        isRemotelySeen,
        isRemotelyFlagged: null,
        keyword: null,
        hasAttachments: null,
        direction,
        mark);

    private static MailboxScope Scope() =>
        MailboxScope.Create([MailAccountId.Create("acct-1"), MailAccountId.Create("acct-2")], selectedFolders: null);
}
