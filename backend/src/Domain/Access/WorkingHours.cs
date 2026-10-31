// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Access;

/// <summary>When one person works: a set of weekdays and one range of hours those days share, on their own wall clock.</summary>
/// <remarks>
/// <para>
/// The hours carry no zone of their own. They are always read in the zone the person's record states, so one statement
/// of eight to five means eight to five in Warsaw for somebody in Warsaw and eight to five in London for somebody in
/// London — which is what lets an organization state one default for people in several places.
/// </para>
/// <para>
/// One range for every day, with its start before its end. A range across midnight and different hours on different
/// days are not supported; both belong to shifts, and are worth modelling when somebody asks for them.
/// </para>
/// <para>
/// Informational alone: it is what a colleague reads on the person's member contact, and nothing — presence, a
/// notification, an automatic reply — is decided by it.
/// </para>
/// </remarks>
public sealed class WorkingHours
{
    private WorkingHours(IReadOnlyList<DayOfWeek> days, TimeOnly start, TimeOnly end)
    {
        this.Days = days;
        this.Start = start;
        this.End = end;
    }

    /// <summary>Gets the days worked, each once, Monday first.</summary>
    public IReadOnlyList<DayOfWeek> Days { get; }

    /// <summary>Gets the time work starts at on each of <see cref="Days" />.</summary>
    public TimeOnly Start { get; }

    /// <summary>Gets the time work ends at on each of <see cref="Days" />, which is after <see cref="Start" />.</summary>
    public TimeOnly End { get; }

    /// <summary>States working hours.</summary>
    /// <param name="days">The days worked; a day named twice is one day.</param>
    /// <param name="start">The time work starts at.</param>
    /// <param name="end">The time work ends at.</param>
    /// <returns>The working hours.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="days" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when no day is named, a value names no weekday, or <paramref name="end" /> is not after <paramref name="start" />; the message is the rule, worded for whoever stated the hours.</exception>
    public static WorkingHours Create(IEnumerable<DayOfWeek> days, TimeOnly start, TimeOnly end)
    {
        ArgumentNullException.ThrowIfNull(days);

        DayOfWeek[] worked = [.. days.Distinct().OrderBy(static day => ((int)day + 6) % 7)];

        if (worked.Length == 0)
        {
            throw new ArgumentException("Working hours name at least one day.");
        }

        if (worked.Any(static day => !Enum.IsDefined(day)))
        {
            throw new ArgumentException("Every working day is a day of the week.");
        }

        if (end <= start)
        {
            throw new ArgumentException(
                "Working hours end after they start on the same day; a range across midnight is not supported.");
        }

        return new WorkingHours(worked, start, end);
    }
}
