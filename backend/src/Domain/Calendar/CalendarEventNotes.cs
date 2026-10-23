// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Calendar;

/// <summary>Holds what an event is about, in the words of the person who put it on the calendar.</summary>
/// <remarks>
/// <para>
/// The title names an event at a glance; the notes are where the agenda, the dial-in, or the reason for it go. Line
/// breaks and tabs are kept because notes are written to be read as somebody wrote them, and every other character
/// that carries no glyph of its own is refused for the reason a title refuses all of them;
/// <see cref="CalendarText" /> holds which ones those are.
/// </para>
/// <para>
/// This is the freest text an event carries and so the likeliest to say something somebody would not want repeated.
/// Nothing logs it, records it as a metric dimension, or writes it into a failure message;
/// <see cref="CalendarEventId" /> is what a failure names.
/// </para>
/// </remarks>
public readonly record struct CalendarEventNotes
{
    /// <summary>The greatest length notes may carry.</summary>
    /// <remarks>
    /// Long enough for the paragraphs somebody actually writes about a meeting, and bounded because the value is
    /// unstructured text a person controls: without a limit an event would become a place to keep documents.
    /// </remarks>
    public const int MaximumLength = 4_000;

    private CalendarEventNotes(string value) => this.Value = value;

    /// <summary>Gets the notes as they were written, trimmed of surrounding whitespace.</summary>
    public string Value { get; }

    /// <summary>Gets whether this value carries notes, which the default of the struct does not.</summary>
    public bool IsSpecified => this.Value is not null;

    /// <summary>Reads notes, refusing text that is blank, too long, or carrying a character that renders as nothing other than a line break or a tab.</summary>
    /// <remarks>
    /// Blank text is not notes: an event without notes holds none at all, so whoever accepts a blank value from a
    /// person reads it as that absence rather than as notes to store.
    /// </remarks>
    /// <param name="value">The notes as supplied.</param>
    /// <param name="notes">The notes, when they are some.</param>
    /// <returns><see langword="true" /> when <paramref name="value" /> is notes.</returns>
    public static bool TryCreate(string? value, out CalendarEventNotes notes)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaximumLength || !CalendarText.IsReadableAcrossLines(trimmed))
        {
            notes = default;

            return false;
        }

        notes = new CalendarEventNotes(trimmed);

        return true;
    }

    /// <summary>Reads notes that are expected to be some.</summary>
    /// <param name="value">The notes as supplied.</param>
    /// <returns>Validated notes.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is not notes.</exception>
    public static CalendarEventNotes Create(string? value) =>
        TryCreate(value, out var notes)
            ? notes
            : throw new ArgumentException(
                $"Event notes are non-blank, at most {MaximumLength} characters, and carry no control or format "
                + "character other than a line break or a tab.",
                nameof(value));
}
