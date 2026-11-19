// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Calendar;
using MailFathom.Domain.Emails;

namespace MailFathom.Domain.Tasks;

/// <summary>Names the conversation or the event a task is about, with the words it was shown under.</summary>
/// <remarks>
/// <para>
/// A task is anchored to one of two kinds of thing, and the type is closed over them: the two factories are the only
/// way to build one, so a value is always a thread and its identifier or an event and its identifier, never both and
/// never neither. A plain enumeration of the kinds is enough, because a kind carries nothing of its own.
/// </para>
/// <para>
/// The label is a copy of a subject or a title taken when the task was anchored, so the list can draw the anchor
/// without opening the thing it points at, and it survives that thing being erased.
/// </para>
/// </remarks>
public sealed record TaskAnchor
{
    /// <summary>The longest label stored.</summary>
    public const int MaximumLabelLength = 200;

    private TaskAnchor(TaskAnchorKind kind, EmailThreadId? threadId, CalendarEventId? eventId, string label)
    {
        this.Kind = kind;
        this.ThreadId = threadId;
        this.EventId = eventId;
        this.Label = label;
    }

    /// <summary>Gets what the anchor points at.</summary>
    public TaskAnchorKind Kind { get; }

    /// <summary>Gets the conversation, and <see langword="null" /> unless <see cref="Kind" /> is <see cref="TaskAnchorKind.Thread" />.</summary>
    public EmailThreadId? ThreadId { get; }

    /// <summary>Gets the event, and <see langword="null" /> unless <see cref="Kind" /> is <see cref="TaskAnchorKind.Event" />.</summary>
    public CalendarEventId? EventId { get; }

    /// <summary>Gets the trimmed words the anchor is drawn with.</summary>
    public string Label { get; }

    /// <summary>Anchors a task to a mail conversation.</summary>
    /// <param name="threadId">The conversation.</param>
    /// <param name="label">The words to draw it with, such as the thread's subject.</param>
    /// <returns>The anchor.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="threadId" /> is the struct default or <paramref name="label" /> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="label" /> exceeds <see cref="MaximumLabelLength" />.</exception>
    public static TaskAnchor Thread(EmailThreadId threadId, string label)
    {
        if (threadId.Value == Guid.Empty)
        {
            throw new ArgumentException("An anchor names a specified thread.", nameof(threadId));
        }

        return new TaskAnchor(TaskAnchorKind.Thread, threadId, eventId: null, BoundedLabel(label));
    }

    /// <summary>Anchors a task to a calendar event.</summary>
    /// <param name="eventId">The event.</param>
    /// <param name="label">The words to draw it with, such as the event's title.</param>
    /// <returns>The anchor.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="eventId" /> is the struct default or <paramref name="label" /> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="label" /> exceeds <see cref="MaximumLabelLength" />.</exception>
    public static TaskAnchor Event(CalendarEventId eventId, string label)
    {
        if (!eventId.IsSpecified)
        {
            throw new ArgumentException("An anchor names a specified event.", nameof(eventId));
        }

        return new TaskAnchor(TaskAnchorKind.Event, threadId: null, eventId, BoundedLabel(label));
    }

    private static string BoundedLabel(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        var trimmed = label.Trim();

        ArgumentOutOfRangeException.ThrowIfGreaterThan(trimmed.Length, MaximumLabelLength, nameof(label));

        return trimmed;
    }
}
