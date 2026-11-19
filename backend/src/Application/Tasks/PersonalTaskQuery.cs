// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Calendar;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Tasks;

namespace MailFathom.Application.Tasks;

/// <summary>Narrows one person's task list to what a surface draws.</summary>
/// <param name="Origin">The origin to read, or <see langword="null" /> for the whole list.</param>
/// <param name="Thread">The thread a task is anchored to, or <see langword="null" /> for any.</param>
/// <param name="Event">The calendar event a task is anchored to, or <see langword="null" /> for any.</param>
/// <param name="Counterpart">The person a task is with, compared by normalized address, or <see langword="null" /> for anybody.</param>
/// <param name="From">The instant a window opens, stated together with <paramref name="Until" />.</param>
/// <param name="Until">The instant a window closes, which a plan starting exactly there falls outside.</param>
/// <remarks>
/// <para>
/// Every narrowing that is stated applies at once. A window matches a task in either of two ways, because a calendar
/// draws a task twice: on the day it is due, read in the offset <paramref name="From" /> carries, and at the time it is
/// planned for, which may fall on another day altogether. A task due Thursday and planned Friday is therefore in the
/// window of either day.
/// </para>
/// <para>
/// The window is half-open and at most <see cref="LongestWindowDays" /> long, which is two months of a calendar and
/// keeps what one read can ask for bounded however the list grows.
/// </para>
/// </remarks>
public sealed record PersonalTaskQuery(
    PersonalTaskOrigin? Origin,
    EmailThreadId? Thread = null,
    CalendarEventId? Event = null,
    EmailAddress? Counterpart = null,
    DateTimeOffset? From = null,
    DateTimeOffset? Until = null)
{
    /// <summary>The longest window one read may ask for.</summary>
    public const int LongestWindowDays = 62;

    /// <summary>Gets the first and last due days the window reaches, read in the offset its opening carries.</summary>
    /// <remarks>
    /// The last day is the one the instant before <see cref="Until" /> falls on, so a window closing at midnight does not
    /// reach the day it closes on. It is <see langword="null" /> where the query states no window.
    /// </remarks>
    public (DateOnly First, DateOnly Last)? DueDays => this.From is { } from && this.Until is { } until
        ? (DateOnly.FromDateTime(from.DateTime), DateOnly.FromDateTime(until.ToOffset(from.Offset).AddTicks(-1).DateTime))
        : null;

    /// <summary>Refuses a query no store should answer.</summary>
    /// <exception cref="ArgumentException">Thrown when <see cref="Origin" /> is not a declared origin, when an anchor or counterpart is the struct default, when only one bound of the window is stated, or when the window closes at or before the instant it opens or is longer than <see cref="LongestWindowDays" />.</exception>
    public void EnsureAnswerable()
    {
        if (this.Origin is { } origin && !Enum.IsDefined(origin))
        {
            throw new ArgumentException("A task list is read under a declared origin.");
        }

        if ((this.Thread is { } thread && thread.Value == Guid.Empty)
            || this.Event is { IsSpecified: false }
            || this.Counterpart is { NormalizedAddress: null })
        {
            throw new ArgumentException("A task list is narrowed to a specified thread, event, or person.");
        }

        if (this.From.HasValue != this.Until.HasValue)
        {
            throw new ArgumentException("A window states where it opens and where it closes.");
        }

        if (this.From is { } from
            && this.Until is { } until
            && (until <= from || until - from > TimeSpan.FromDays(LongestWindowDays)))
        {
            throw new ArgumentException("A window closes after it opens and spans at most sixty-two days.");
        }
    }
}
