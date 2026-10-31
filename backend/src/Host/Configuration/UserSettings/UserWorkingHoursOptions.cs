// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using MailFathom.Domain.Access;

namespace MailFathom.Host.Configuration.UserSettings;

/// <summary>When a user works, as their record states it and before any rule judged it.</summary>
/// <remarks>
/// <para>
/// A block rather than one value, so a settings policy resolves each of its three keys on its own: an organization
/// defaulting Monday to Friday, eight to five, lets a member who states only their own <c>Start</c> and <c>End</c> keep
/// the organization's days. The rules are therefore judged on the record a user is served, never on what one layer
/// states alone, and a layer stating only some of the keys is ordinary.
/// </para>
/// <para>
/// Carried as written rather than as resolved days and times, for the reason the record's own language is: the
/// refusals below name the key and the form to write, where typed properties would have answered the same mistake with
/// the configuration binder's own sentence.
/// </para>
/// </remarks>
internal sealed class UserWorkingHoursOptions
{
    private static readonly string[] TimeFormats = ["HH:mm", "H:mm"];

    /// <summary>Gets or sets the days worked, each named in English as <see cref="DayOfWeek" /> names it, such as <c>Monday</c>.</summary>
    public List<string> Days { get; set; } = [];

    /// <summary>Gets or sets the time work starts at on each of <see cref="Days" />, written <c>HH:mm</c>.</summary>
    public string? Start { get; set; }

    /// <summary>Gets or sets the time work ends at on each of <see cref="Days" />, written <c>HH:mm</c>.</summary>
    public string? End { get; set; }

    /// <summary>Says which of the keys this block states is not written the way that key takes, judging each on its own.</summary>
    /// <returns>The rule a stated key breaks, or <see langword="null" /> where every stated key is well written.</returns>
    /// <remarks>
    /// Asked of a policy statement as well as of a record, because a statement may name one key of the three and is
    /// still wrong when that one is misspelled; whether the three together make working hours is asked by
    /// <see cref="Read" /> of the record a user is served, never of one layer.
    /// </remarks>
    internal string? FindMiswrittenKey()
    {
        if (this.Days.Any(static day => ReadDay(day) is null))
        {
            return "Each of Days names a weekday in English, such as 'Monday'.";
        }

        return new[] { this.Start, this.End }.Any(static time => !string.IsNullOrWhiteSpace(time) && !TryReadTime(time.Trim(), out _))
            ? "Start and End are times of day written as 'HH:mm', such as '08:00'."
            : null;
    }

    /// <summary>Reads the working hours this block states.</summary>
    /// <param name="refusal">The rule the block breaks, or <see langword="null" /> where it breaks none.</param>
    /// <returns>The working hours, or <see langword="null" /> where the block states none or breaks a rule.</returns>
    internal WorkingHours? Read(out string? refusal)
    {
        refusal = this.FindMiswrittenKey();

        var start = this.Start?.Trim();
        var end = this.End?.Trim();

        if (refusal is not null || (this.Days.Count == 0 && string.IsNullOrEmpty(start) && string.IsNullOrEmpty(end)))
        {
            return null;
        }

        if (this.Days.Count == 0 || string.IsNullOrEmpty(start) || string.IsNullOrEmpty(end))
        {
            refusal = "Days, Start, and End are stated together, or none of them is.";

            return null;
        }

        TryReadTime(start, out var startsAt);
        TryReadTime(end, out var endsAt);

        try
        {
            return WorkingHours.Create(this.Days.Select(ReadDay).OfType<DayOfWeek>(), startsAt, endsAt);
        }
        catch (ArgumentException broken)
        {
            refusal = broken.Message;

            return null;
        }
    }

    /// <summary>Reads a weekday by its English name, read however it was capitalized and never as a number.</summary>
    private static DayOfWeek? ReadDay(string? written) =>
        Enum.GetValues<DayOfWeek>()
            .Where(day => string.Equals(day.ToString(), written?.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(static day => (DayOfWeek?)day)
            .FirstOrDefault();

    private static bool TryReadTime(string written, out TimeOnly time) =>
        TimeOnly.TryParseExact(written, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}
