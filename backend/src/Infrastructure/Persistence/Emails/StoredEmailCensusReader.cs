// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MailFathom.Application.Emails.Census;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>Counts the local mailbox copy out of PostgreSQL, in total or grouped.</summary>
/// <remarks>
/// <para>
/// The mail is narrowed by <see cref="StoredEmailSelectionPredicate.Matching" /> and nothing of its own, so a count admits
/// exactly what a listing given the same filters would page through. What is counted is the columns a group is keyed by
/// — the normalized sender address, the received instant, and the category records — and never a body or stored content.
/// </para>
/// <para>
/// A sender, a domain, or a period is one statement: the groups are counted, ordered, and cut to the leading ones, and
/// how many groups exist, what they count between them, and how much mail matched are scalar subqueries of the same
/// statement, so the figures agree with each other however much mail arrives meanwhile. Only a scope whose groups are
/// all empty needs the total asked for again, because a statement over no groups returns no row to carry it.
/// </para>
/// <para>
/// A category is grouped by account as well as by name, because what an account shows is its own: a default one account
/// switched off stays recorded and must not be counted under the name another account shows. Those groups are bounded
/// by what the accounts offer rather than by the mailbox, so they are read whole and the hidden ones are left out here.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredEmailCensusReader(MailFathomDbContext dbContext) : IStoredEmailCensusReader
{
    /// <inheritdoc />
    public async Task<MailCensus> CountAsync(
        MailboxEmailSelection selection,
        MailCensusGrouping grouping,
        int leadingGroups,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(grouping);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(leadingGroups);

        var matching = StoredEmailSelectionPredicate.Matching(dbContext.StoredEmails.AsNoTracking(), selection);

        switch (grouping.Dimension)
        {
            case MailCensusDimension.Total:
                return MailCensus.Empty with { Total = await matching.CountAsync(cancellationToken) };

            case MailCensusDimension.Category:
                var categories = await CategoryGroups(matching, grouping).ToArrayAsync(cancellationToken);

                return CategoryCensusOf(categories, grouping, leadingGroups, await matching.CountAsync(cancellationToken));

            default:
                var rows = await LeadingGroups(matching, grouping, leadingGroups).ToArrayAsync(cancellationToken);

                return rows.Length > 0
                    ? CensusOf(rows, grouping.Dimension)
                    : MailCensus.Empty with { Total = await matching.CountAsync(cancellationToken) };
        }
    }

    /// <summary>Composes the one statement a census by sender, domain, or period is read from.</summary>
    /// <param name="matching">The mail the selection admits.</param>
    /// <param name="grouping">What the mail is grouped by, which is neither nothing nor a category.</param>
    /// <param name="leadingGroups">How many groups the statement returns at most.</param>
    /// <returns>The leading groups, each row also carrying the figures for every group and for the whole selection.</returns>
    /// <remarks>Exposed so a test can read the command, and so the evaluation suite counts its corpus with the deployment's own composition.</remarks>
    internal static IQueryable<MailCensusRow> LeadingGroups(
        IQueryable<StoredEmailEntity> matching,
        MailCensusGrouping grouping,
        int leadingGroups) => grouping.Dimension switch
        {
            MailCensusDimension.SenderAddress => Ranked(matching.Select(static email => email.SenderNormalizedAddress), matching, leadingGroups),
            MailCensusDimension.SenderDomain => Ranked(SenderDomains(matching), matching, leadingGroups),
            MailCensusDimension.Day or MailCensusDimension.Week or MailCensusDimension.Month =>
                Chronological(Periods(matching, grouping.Dimension, grouping.Zone.Id), matching, leadingGroups),
            _ => throw new ArgumentOutOfRangeException(nameof(grouping), grouping.Dimension, "Only a sender, a domain, or a period is grouped by one statement."),
        };

    /// <summary>Composes the statement a census by category is read from: one row per account and category the accounts show.</summary>
    /// <param name="matching">The mail the selection admits.</param>
    /// <param name="grouping">The grouping, carrying what each account shows.</param>
    /// <returns>How many messages of each account carry each shown category.</returns>
    internal static IQueryable<MailCategoryCensusRow> CategoryGroups(
        IQueryable<StoredEmailEntity> matching,
        MailCensusGrouping grouping)
    {
        var shownNames = grouping.ShownCategories.Values.SelectMany(static names => names).Distinct(StringComparer.Ordinal).ToArray();

        return from email in matching
               from record in email.Categories
               where shownNames.Contains(record.Category)
               group record by new { email.MailboxAccountId, record.Category } into recorded
               select new MailCategoryCensusRow(recorded.Key.MailboxAccountId, recorded.Key.Category, recorded.Count());
    }

    /// <summary>Reads the rows of one statement as the census they describe.</summary>
    /// <param name="rows">The leading groups, at least one.</param>
    /// <param name="dimension">What the groups are keyed by, which decides how a period's key is written.</param>
    /// <returns>The census.</returns>
    internal static MailCensus CensusOf(IReadOnlyList<MailCensusRow> rows, MailCensusDimension dimension)
    {
        var figures = rows[0];

        return new MailCensus(
            [.. rows.Select(row => new MailCensusGroup(row.Text ?? PeriodKey(row.Period, dimension), row.Count))],
            figures.GroupCount,
            figures.CountedInGroups,
            figures.Total);
    }

    /// <summary>Reads the category rows as the census they describe, leaving out a category its own account does not show.</summary>
    /// <param name="rows">How many messages of each account carry each category.</param>
    /// <param name="grouping">The grouping, carrying what each account shows.</param>
    /// <param name="leadingGroups">How many groups the census carries at most.</param>
    /// <param name="total">How much mail the selection holds.</param>
    /// <returns>The census, the most counted category first and a tie in name order.</returns>
    internal static MailCensus CategoryCensusOf(
        IReadOnlyList<MailCategoryCensusRow> rows,
        MailCensusGrouping grouping,
        int leadingGroups,
        int total)
    {
        var groups = rows
            .Where(row => grouping.ShownCategories.TryGetValue(MailAccountId.Create(row.MailboxAccountId), out var shown)
                && shown.Contains(row.Category, StringComparer.Ordinal))
            .GroupBy(static row => row.Category, StringComparer.Ordinal)
            .Select(static category => new MailCensusGroup(category.Key, category.Sum(static row => row.Count)))
            .OrderByDescending(static group => group.Count)
            .ThenBy(static group => group.Key, StringComparer.Ordinal)
            .ToArray();

        return new MailCensus([.. groups.Take(leadingGroups)], groups.Length, groups.Sum(static group => group.Count), total);
    }

    /// <summary>Keys each message by the domain of its sender's address.</summary>
    /// <remarks>
    /// The normalized address carries exactly one <c>@</c>, so what follows the first is the domain. The culture-free
    /// overload of <see cref="string.IndexOf(string, StringComparison)" /> has no translation in this provider; the one
    /// written here is translated to <c>strpos</c>, which compares code points rather than anything a culture decides.
    /// </remarks>
    [SuppressMessage("Globalization", "CA1310:Specify StringComparison for correctness", Justification = "Translated to strpos, which no culture reaches; an @ is no character a culture ignores when this runs in memory.")]
    private static IQueryable<string?> SenderDomains(IQueryable<StoredEmailEntity> matching) =>
        matching.Select(static email => email.SenderNormalizedAddress == null
            ? null
            : email.SenderNormalizedAddress.Substring(email.SenderNormalizedAddress.IndexOf("@") + 1));

    /// <summary>Keys each message by the first day of the period it arrived in, read on the person's clock.</summary>
    /// <remarks>
    /// The instant is moved to the person's wall clock with <c>AT TIME ZONE</c> before it is cut, so mail that arrived
    /// shortly after midnight there counts on the day it arrived for them rather than on the day it was in UTC. A week
    /// starts on Monday. Undated mail is keyed by nothing, which makes it a group of its own.
    /// </remarks>
    private static IQueryable<DateTime?> Periods(
        IQueryable<StoredEmailEntity> matching,
        MailCensusDimension dimension,
        string zoneId)
    {
        var local = matching.Select(email => email.ReceivedAt == null
            ? (DateTime?)null
            : TimeZoneInfo.ConvertTimeBySystemTimeZoneId(email.ReceivedAt.Value.UtcDateTime, zoneId));

        return dimension switch
        {
            MailCensusDimension.Day => local.Select(static at => at == null ? null : (DateTime?)at.Value.Date),
            MailCensusDimension.Week => local.Select(static at => at == null
                ? null
                : (DateTime?)at.Value.Date.AddDays(-(((int)at.Value.DayOfWeek + 6) % 7))),
            _ => local.Select(static at => at == null ? null : (DateTime?)at.Value.Date.AddDays(1 - at.Value.Day)),
        };
    }

    /// <summary>Counts the groups and leads with the largest, a tie in key order and the group keyed by nothing last.</summary>
    private static IQueryable<MailCensusRow> Ranked(
        IQueryable<string?> keys,
        IQueryable<StoredEmailEntity> matching,
        int leadingGroups)
    {
        var groups = keys.GroupBy(static key => key).Select(static group => new { group.Key, Count = group.Count() });

        return groups
            .OrderByDescending(static group => group.Count)
            .ThenBy(static group => group.Key == null)
            .ThenBy(static group => group.Key)
            .Take(leadingGroups)
            .Select(group => new MailCensusRow(
                group.Key,
                null,
                group.Count,
                groups.Count(),
                groups.Sum(static other => other.Count),
                matching.Count()));
    }

    /// <summary>Counts the periods and leads with the latest, undated mail last.</summary>
    private static IQueryable<MailCensusRow> Chronological(
        IQueryable<DateTime?> keys,
        IQueryable<StoredEmailEntity> matching,
        int leadingGroups)
    {
        var groups = keys.GroupBy(static key => key).Select(static group => new { group.Key, Count = group.Count() });

        return groups
            .OrderBy(static group => group.Key == null)
            .ThenByDescending(static group => group.Key)
            .Take(leadingGroups)
            .Select(group => new MailCensusRow(
                null,
                group.Key,
                group.Count,
                groups.Count(),
                groups.Sum(static other => other.Count),
                matching.Count()));
    }

    private static string? PeriodKey(DateTime? period, MailCensusDimension dimension) => period switch
    {
        null => null,
        { } start when dimension is MailCensusDimension.Month => start.ToString("yyyy-MM", CultureInfo.InvariantCulture),
        { } start => start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
    };
}
