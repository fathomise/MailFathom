// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Census;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Emails;

/// <summary>
/// Covers what a census asks PostgreSQL for, which the C# it is written in does not show. A grouping evaluated in this
/// process, a period cut in UTC, or a ceiling applied after materialization each still returns plausible figures, so the
/// generated command is what is read rather than a result.
/// </summary>
public sealed class StoredEmailCensusReaderCommandTests
{
    private static readonly MailAccountId Work = MailAccountId.Create("work");

    private static readonly MailAccountId Home = MailAccountId.Create("home");

    /// <summary>Every grouping is counted, ordered, and cut by the database in one command, which is what keeps a census bounded.</summary>
    [Theory]
    [InlineData(MailCensusDimension.SenderAddress)]
    [InlineData(MailCensusDimension.SenderDomain)]
    [InlineData(MailCensusDimension.Day)]
    [InlineData(MailCensusDimension.Week)]
    [InlineData(MailCensusDimension.Month)]
    public void LeadingGroups_EachGrouping_GroupsCountsAndCutsInTheDatabase(MailCensusDimension dimension)
    {
        // Act
        var command = LeadingGroupsCommand(dimension);

        // Assert
        Assert.Contains("GROUP BY", command, StringComparison.Ordinal);
        Assert.Contains("count(*)", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", command, StringComparison.Ordinal);
    }

    /// <summary>A period is read on the person's clock, so the instant is moved to their zone before the database cuts it.</summary>
    [Theory]
    [InlineData(MailCensusDimension.Day)]
    [InlineData(MailCensusDimension.Week)]
    [InlineData(MailCensusDimension.Month)]
    public void LeadingGroups_APeriod_CutsTheInstantOnThePersonsClock(MailCensusDimension dimension)
    {
        // Act
        var command = LeadingGroupsCommand(dimension);

        // Assert
        Assert.Contains("AT TIME ZONE", command, StringComparison.Ordinal);
        Assert.Contains("date_trunc", command, StringComparison.Ordinal);
    }

    /// <summary>A domain is cut out of the address by the database, so mail is grouped by organisation without a row reaching this process.</summary>
    [Fact]
    public void LeadingGroups_ByDomain_CutsTheDomainOutOfTheAddressInTheDatabase()
    {
        // Act
        var command = LeadingGroupsCommand(MailCensusDimension.SenderDomain);

        // Assert
        Assert.Contains("strpos", command, StringComparison.Ordinal);
        Assert.Contains(nameof(StoredEmailEntity.SenderNormalizedAddress), command, StringComparison.Ordinal);
    }

    /// <summary>A count admits exactly what a listing would, and leaving tombstoned mail out is the part a count is easiest to write without.</summary>
    [Fact]
    public void LeadingGroups_Always_NarrowsByWhatAListingAdmits()
    {
        // Act
        var command = LeadingGroupsCommand(MailCensusDimension.SenderAddress);

        // Assert
        Assert.Contains(nameof(StoredEmailEntity.RemoteExpungeObservedAt), command, StringComparison.Ordinal);
        Assert.Contains(nameof(StoredEmailEntity.MailboxAccountId), command, StringComparison.Ordinal);
    }

    /// <summary>A census reads the columns a group is keyed by and nothing a message says.</summary>
    [Theory]
    [InlineData(MailCensusDimension.SenderAddress)]
    [InlineData(MailCensusDimension.Month)]
    public void LeadingGroups_Always_ReadsNoBodyAndNoSubject(MailCensusDimension dimension)
    {
        // Act
        var command = LeadingGroupsCommand(dimension);

        // Assert
        Assert.DoesNotContain("BodyText", command, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(StoredEmailEntity.Subject), command, StringComparison.Ordinal);
    }

    /// <summary>The categories are grouped by the database too, and only the names some account shows are read.</summary>
    [Fact]
    public void CategoryGroups_Always_GroupsTheShownCategoriesInTheDatabase()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        // Act
        var command = StoredEmailCensusReader.CategoryGroups(Matching(context), GroupingBy(MailCensusDimension.Category)).ToQueryString();

        // Assert
        Assert.Contains("GROUP BY", command, StringComparison.Ordinal);
        Assert.Contains(nameof(EmailCategoryEntity.Category), command, StringComparison.Ordinal);
    }

    /// <summary>
    /// What one account shows is its own: a default it switched off stays recorded, and the count of it is left out even
    /// where another account shows a category of the same name, whose mail still counts under it.
    /// </summary>
    [Fact]
    public void CategoryCensusOf_ACategoryOneAccountHides_CountsOnlyTheAccountsShowingIt()
    {
        // Arrange
        MailCategoryCensusRow[] rows =
        [
            new(Work.Value, "Invoices", 4),
            new(Home.Value, "Invoices", 3),
            new(Home.Value, "Promotions", 9),
            new(Work.Value, "Promotions", 2),
        ];
        var grouping = new MailCensusGrouping(
            MailCensusDimension.Category,
            UserTimeZone.Coordinated,
            new Dictionary<MailAccountId, IReadOnlyList<string>>
            {
                [Work] = ["Invoices", "Promotions"],
                [Home] = ["Invoices"],
            });

        // Act
        var census = StoredEmailCensusReader.CategoryCensusOf(rows, grouping, leadingGroups: 5, total: 12);

        // Assert
        Assert.Equal([new MailCensusGroup("Invoices", 7), new MailCensusGroup("Promotions", 2)], census.Groups);
        Assert.Equal((2, 9, 12), (census.GroupCount, census.CountedInGroups, census.Total));
    }

    /// <summary>The ceiling cuts the categories as it cuts any group, and what lies beyond them is still stated.</summary>
    [Fact]
    public void CategoryCensusOf_MoreCategoriesThanTheCeiling_LeadsWithTheLargestAndCountsTheRest()
    {
        // Arrange
        MailCategoryCensusRow[] rows = [new(Work.Value, "Invoices", 4), new(Work.Value, "Travel", 6), new(Work.Value, "Updates", 1)];
        var grouping = new MailCensusGrouping(
            MailCensusDimension.Category,
            UserTimeZone.Coordinated,
            new Dictionary<MailAccountId, IReadOnlyList<string>> { [Work] = ["Invoices", "Travel", "Updates"] });

        // Act
        var census = StoredEmailCensusReader.CategoryCensusOf(rows, grouping, leadingGroups: 1, total: 11);

        // Assert
        Assert.Equal((new MailCensusGroup("Travel", 6), 2, 5), (Assert.Single(census.Groups), census.GroupsBeyond, census.CountedBeyond));
    }

    /// <summary>A period's key is the first day it covers, a month written without its day, and undated mail keyed by nothing.</summary>
    [Theory]
    [InlineData(MailCensusDimension.Day, "2026-09-07")]
    [InlineData(MailCensusDimension.Week, "2026-09-07")]
    [InlineData(MailCensusDimension.Month, "2026-09")]
    public void CensusOf_APeriod_WritesTheKeyAsTheFirstDayItCovers(MailCensusDimension dimension, string key)
    {
        // Arrange
        MailCensusRow[] rows = [new(null, new DateTime(2026, 9, 7), 3, 2, 4, 4), new(null, null, 1, 2, 4, 4)];

        // Act
        var census = StoredEmailCensusReader.CensusOf(rows, dimension);

        // Assert
        Assert.Equal([new MailCensusGroup(key, 3), new MailCensusGroup(null, 1)], census.Groups);
        Assert.Equal((2, 4, 4), (census.GroupCount, census.CountedInGroups, census.Total));
    }

    private static string LeadingGroupsCommand(MailCensusDimension dimension)
    {
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        return StoredEmailCensusReader.LeadingGroups(Matching(context), GroupingBy(dimension), leadingGroups: 20).ToQueryString();
    }

    private static IQueryable<StoredEmailEntity> Matching(MailFathomDbContext context) =>
        StoredEmailSelectionPredicate.Matching(
            context.StoredEmails.AsNoTracking(),
            MailboxEmailSelection.Create(
                MailboxScope.Create([Work], [MailFolderId.Create(Guid.Parse("0199c1a2-0000-7000-8000-00000000c0de"))]),
                senderAddress: null,
                recipientAddress: null,
                subjectFragment: null,
                receivedOnOrAfter: null,
                receivedBefore: null,
                isMarkedSeen: null,
                isFlagged: null,
                keyword: null,
                hasAttachments: null));

    private static MailCensusGrouping GroupingBy(MailCensusDimension dimension)
    {
        Assert.True(UserTimeZone.TryRead("Europe/Warsaw", out var warsaw));

        return new MailCensusGrouping(
            dimension,
            warsaw,
            new Dictionary<MailAccountId, IReadOnlyList<string>> { [Work] = ["Invoices"] });
    }
}
