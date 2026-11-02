// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.BrowseTimeline;

/// <summary>Pages a timeline in both directions from a cursor, whether its rows are messages or the conversations they group into.</summary>
/// <remarks>
/// <para>
/// Scrolling back is a second walk over the same list rather than a second list. The order the rows are sorted in is
/// part of what a cursor was issued for, so it never changes with the direction a page is asked in: a backward page is
/// read away from the cursor with the walk reversed and handed back in the sorted order, and its cursors carry the
/// sorted list's fingerprint. That is what lets a client page in both directions from cursors it collected in either.
/// </para>
/// <para>
/// A row's position is a message's either way. A conversation stands in the list at its newest message the read let
/// through, so the cursor of a grouped page is the cursor of a message page; only the fingerprint tells the two lists
/// apart.
/// </para>
/// </remarks>
internal static class TimelinePaging
{
    /// <summary>How this use case names the value that says which way a page continues, when it refuses one.</summary>
    private const string PageDirectionFilterName = "page direction";

    /// <summary>Establishes that the page direction names one of the two answers rather than an unmapped value.</summary>
    /// <param name="pageDirection">The direction the request named.</param>
    /// <returns>The same direction.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="pageDirection" /> is not a defined member.</exception>
    internal static TimelinePageDirection Defined(TimelinePageDirection pageDirection) =>
        Enum.IsDefined(pageDirection)
            ? pageDirection
            : throw new ArgumentOutOfRangeException(
                nameof(pageDirection),
                pageDirection,
                "A page continues from its cursor in one of two directions, and no other value names one.");

    /// <summary>Reads the position a cursor names, after establishing that the cursor belongs to this list.</summary>
    /// <param name="cursor">The cursor the request carried.</param>
    /// <param name="fingerprint">The fingerprint of the list the request reads.</param>
    /// <param name="pageDirection">The direction the page continues in.</param>
    /// <returns>The position, or <see langword="null" /> for the leading end of the list.</returns>
    /// <exception cref="MailboxQueryFilterInvalidException">Thrown when a backward page is asked for without a cursor.</exception>
    /// <exception cref="MailboxQueryCursorMalformedException">Thrown when the cursor is not one this system issued.</exception>
    /// <exception cref="MailboxQueryCursorFilterMismatchException">Thrown when the cursor was issued for a different list.</exception>
    /// <remarks>
    /// A blank cursor is the leading end of the list rather than a malformed one, for the reason the tool listing reads
    /// it that way: a client carrying the field with nothing in it yet has asked for the beginning of the walk. That
    /// makes it no cursor at all, which is why a backward page is refused against it as well as against an absent one.
    /// </remarks>
    internal static EmailTimelinePosition? ContinuationBoundary(
        string? cursor,
        string fingerprint,
        TimelinePageDirection pageDirection)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            if (pageDirection is TimelinePageDirection.Backward)
            {
                throw MailboxQueryFilterInvalidException.NeedsACursor(PageDirectionFilterName);
            }

            return null;
        }

        if (!EmailTimelineCursor.TryDecode(cursor, out var decodedCursor))
        {
            throw new MailboxQueryCursorMalformedException();
        }

        if (!string.Equals(decodedCursor.FilterFingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw new MailboxQueryCursorFilterMismatchException();
        }

        return decodedCursor.Position;
    }

    /// <summary>Names the walk that produces the page, which is the sorted list itself or that list read backwards.</summary>
    /// <param name="sortedList">The list as it is sorted.</param>
    /// <param name="pageDirection">The direction the page continues in.</param>
    /// <returns>The filter the walk reads under.</returns>
    internal static EmailTimelineFilter WalkedIn(EmailTimelineFilter sortedList, TimelinePageDirection pageDirection) =>
        pageDirection is TimelinePageDirection.Forward
            ? sortedList
            : EmailTimelineFilter.ReadIn(sortedList.Selection, Opposite(sortedList.Direction));

    /// <summary>Cuts the walk to one page and puts it back into the order the list is sorted in.</summary>
    /// <typeparam name="TRow">What one row of the list is.</typeparam>
    /// <param name="walked">The rows the walk returned, one beyond the page where more exist.</param>
    /// <param name="pageSize">How many rows the page holds.</param>
    /// <param name="pageDirection">The direction the page was walked in.</param>
    /// <returns>The page, in the sorted order.</returns>
    /// <remarks>
    /// The probe row is dropped first, so reversing a backward walk turns the page the caller keeps rather than a page
    /// with somebody else's boundary row still on the end of it.
    /// </remarks>
    internal static IReadOnlyList<TRow> SortedPage<TRow>(
        IReadOnlyList<TRow> walked,
        MailboxQueryPageSize pageSize,
        TimelinePageDirection pageDirection)
    {
        var withoutTheProbe = walked.Take(pageSize.Value);

        return pageDirection is TimelinePageDirection.Forward
            ? [.. withoutTheProbe]
            : [.. withoutTheProbe.Reverse()];
    }

    /// <summary>Issues the cursor that reads the page after this one, or nothing where the list ends here.</summary>
    /// <param name="page">The positions of the page's rows, in the sorted order.</param>
    /// <param name="fingerprint">The fingerprint of the sorted list.</param>
    /// <param name="pageDirection">The direction the page was walked in.</param>
    /// <param name="beyondThePage">Whether the walk returned a row beyond the page.</param>
    /// <returns>The cursor, or <see langword="null" />.</returns>
    /// <remarks>
    /// Walking forward, another page exists exactly when the probe row came back. Walking backward there is always
    /// something after the page, because the cursor the walk started from names a row that is still there.
    /// </remarks>
    internal static string? CursorAfterThePage(
        IReadOnlyList<EmailTimelinePosition> page,
        string fingerprint,
        TimelinePageDirection pageDirection,
        bool beyondThePage)
    {
        if (page.Count is 0)
        {
            return null;
        }

        var more = pageDirection is not TimelinePageDirection.Forward || beyondThePage;

        return more ? EmailTimelineCursor.After(page[^1], fingerprint).Encode() : null;
    }

    /// <summary>Issues the cursor that reads the page before this one, or nothing where the list begins here.</summary>
    /// <param name="page">The positions of the page's rows, in the sorted order.</param>
    /// <param name="fingerprint">The fingerprint of the sorted list.</param>
    /// <param name="pageDirection">The direction the page was walked in.</param>
    /// <param name="beyondThePage">Whether the walk returned a row beyond the page.</param>
    /// <param name="boundary">The position the request continued from, or <see langword="null" /> for the leading end.</param>
    /// <returns>The cursor, or <see langword="null" />.</returns>
    /// <remarks>
    /// Walking backward, a further page exists exactly when the probe row came back. Walking forward, what lies before
    /// the page is whatever the presented cursor was taken from — so a page read from the leading end of the list has
    /// nothing before it and every other forward page has.
    /// </remarks>
    internal static string? CursorBeforeThePage(
        IReadOnlyList<EmailTimelinePosition> page,
        string fingerprint,
        TimelinePageDirection pageDirection,
        bool beyondThePage,
        EmailTimelinePosition? boundary)
    {
        if (page.Count is 0)
        {
            return null;
        }

        var more = pageDirection is TimelinePageDirection.Forward
            ? boundary is not null
            : beyondThePage;

        return more ? EmailTimelineCursor.After(page[0], fingerprint).Encode() : null;
    }

    /// <summary>Names the end of the timeline a backward walk reads towards.</summary>
    private static EmailTimelineDirection Opposite(EmailTimelineDirection direction) =>
        direction is EmailTimelineDirection.NewestFirst
            ? EmailTimelineDirection.OldestFirst
            : EmailTimelineDirection.NewestFirst;
}
