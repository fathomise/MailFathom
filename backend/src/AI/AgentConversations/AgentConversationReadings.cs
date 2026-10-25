// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using MailFathom.AI.Orchestration;
using MailFathom.Application.Discovery.Presentation.Citations;
using MailFathom.Application.Emails.Provenance;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Tasks;

namespace MailFathom.AI.AgentConversations;

/// <summary>Writes what the Agent's reading tools return: a conversation, a stretch of the calendar, and the task list.</summary>
/// <remarks>
/// <para>
/// Each is a document written through <see cref="AgentTurnDocument" />, because each carries text somebody else wrote
/// — a sender's subject and body, the title of an event an invitation put on the calendar, a task mail proposed. What
/// this service knows about each record — its identifier, the source the answer cites it by, when it happens, whether
/// it is done, where it came from — is an attribute, so no title can claim a state or an origin it does not have.
/// </para>
/// <para>
/// The values arrive already guarded, one by one, and the types say so: what the egress guard scans is a value rather
/// than a document composed around it, so a redaction can never reach into the elements written here.
/// </para>
/// </remarks>
internal static class AgentConversationReadings
{
    /// <summary>Names the element a conversation is read into.</summary>
    internal const string ThreadElementName = "thread";

    /// <summary>Names the element one message of a conversation occupies.</summary>
    internal const string MessageElementName = "message";

    /// <summary>Names the attribute carrying the identifier a proposal names a message by.</summary>
    internal const string MessageIdAttributeName = "id";

    /// <summary>Names the attribute carrying the source an answer cites a message as.</summary>
    internal const string SourceAttributeName = "source";

    /// <summary>Names the attribute carrying the address a message was sent from.</summary>
    internal const string FromAttributeName = "from";

    /// <summary>Names the attribute carrying when a message was sent.</summary>
    internal const string SentAttributeName = "sent";

    /// <summary>Names the element carrying a message's subject.</summary>
    internal const string SubjectElementName = "subject";

    /// <summary>Names the element carrying a message's body.</summary>
    internal const string BodyElementName = "body";

    /// <summary>Names the element a stretch of the calendar is read into.</summary>
    internal const string CalendarElementName = "calendar";

    /// <summary>Names the attribute carrying where a window or an event opens.</summary>
    internal const string StartAttributeName = "start";

    /// <summary>Names the attribute carrying where a window or an event closes.</summary>
    internal const string EndAttributeName = "end";

    /// <summary>Names the element one calendar event occupies.</summary>
    internal const string EventElementName = "event";

    /// <summary>Names the attribute saying an event is a whole day rather than a clock time.</summary>
    internal const string AllDayAttributeName = "all-day";

    /// <summary>Names the element carrying an event's or a task's title.</summary>
    internal const string TitleElementName = "title";

    /// <summary>Names the element the task list is read into.</summary>
    internal const string TasksElementName = "tasks";

    /// <summary>Names the element one task occupies.</summary>
    internal const string TaskElementName = "task";

    /// <summary>Names the attribute carrying whether a task is done.</summary>
    internal const string StateAttributeName = "state";

    /// <summary>The value <see cref="StateAttributeName" /> carries for a task still to do.</summary>
    internal const string OpenState = "open";

    /// <summary>The value <see cref="StateAttributeName" /> carries for a task already done.</summary>
    internal const string DoneState = "done";

    /// <summary>Names the attribute carrying where a task came from.</summary>
    internal const string OriginAttributeName = "origin";

    /// <summary>The value <see cref="OriginAttributeName" /> carries for a task the person entered or accepted.</summary>
    internal const string PersonOrigin = "person";

    /// <summary>The value <see cref="OriginAttributeName" /> carries for a task mail proposed and nobody has accepted.</summary>
    internal const string ProposedFromMailOrigin = "proposed-from-mail";

    /// <summary>Names the attribute carrying the day a task is due.</summary>
    internal const string DueAttributeName = "due";

    /// <summary>Writes one conversation.</summary>
    /// <param name="messages">The messages shown, in the conversation's order.</param>
    /// <returns>The document.</returns>
    internal static string Thread(IReadOnlyList<GuardedThreadReading> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        using var document = new AgentTurnDocument();

        document.StartElement(ThreadElementName);

        foreach (var message in messages)
        {
            document.StartElement(MessageElementName);
            document.WriteAttribute(MessageIdAttributeName, message.Id.ToString());
            document.WriteAttribute(SourceAttributeName, message.Source.Value);
            WriteOptional(document, FromAttributeName, message.SenderAddress);
            WriteOptional(document, SentAttributeName, Instant(message.SentAt));
            document.WriteProvenance(message.Provenance);

            if (message.Subject is { } subject)
            {
                document.WriteQuoted(SubjectElementName, subject);
            }

            document.WriteQuoted(BodyElementName, message.Body, message.BodyCut);
            document.EndElement();
        }

        return document.Complete();
    }

    /// <summary>Writes one window of the calendar.</summary>
    /// <param name="opens">Where the window opens.</param>
    /// <param name="closes">Where the window closes.</param>
    /// <param name="events">The events in it, earliest first.</param>
    /// <returns>The document.</returns>
    internal static string Calendar(DateTimeOffset opens, DateTimeOffset closes, IReadOnlyList<GuardedCalendarReading> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        using var document = new AgentTurnDocument();

        document.StartElement(CalendarElementName);
        document.WriteAttribute(StartAttributeName, Instant(opens));
        document.WriteAttribute(EndAttributeName, Instant(closes));

        foreach (var entry in events)
        {
            document.StartElement(EventElementName);
            document.WriteAttribute(StartAttributeName, Instant(entry.Start));
            WriteOptional(document, EndAttributeName, Instant(entry.End));

            if (entry.IsAllDay)
            {
                document.WriteAttribute(AllDayAttributeName, "true");
            }

            document.WriteQuoted(TitleElementName, entry.Title);
            document.EndElement();
        }

        return document.Complete();
    }

    /// <summary>Writes the task list.</summary>
    /// <param name="tasks">The tasks, soonest due first.</param>
    /// <returns>The document.</returns>
    internal static string Tasks(IReadOnlyList<GuardedTaskReading> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        using var document = new AgentTurnDocument();

        document.StartElement(TasksElementName);

        foreach (var task in tasks)
        {
            document.StartElement(TaskElementName);
            document.WriteAttribute(StateAttributeName, task.IsCompleted ? DoneState : OpenState);
            document.WriteAttribute(OriginAttributeName, task.Origin is PersonalTaskOrigin.Proposed ? ProposedFromMailOrigin : PersonOrigin);
            WriteOptional(document, DueAttributeName, task.DueOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            document.WriteQuoted(TitleElementName, task.Title);
            document.EndElement();
        }

        return document.Complete();
    }

    /// <summary>Writes an attribute only where there is a value, so the model reads what a record carries rather than blanks to tell apart from it.</summary>
    private static void WriteOptional(AgentTurnDocument document, string attributeName, string? value)
    {
        if (value is not null)
        {
            document.WriteAttribute(attributeName, value);
        }
    }

    private static string Instant(DateTimeOffset instant) => instant.ToString("O", CultureInfo.InvariantCulture);

    private static string? Instant(DateTimeOffset? instant) => instant is { } known ? Instant(known) : null;
}

/// <summary>One message of a conversation as the Agent is shown it, every text of it already guarded.</summary>
/// <param name="Id">The message's identifier, which a proposal names it by.</param>
/// <param name="Source">The source this run declared it as, which an answer cites.</param>
/// <param name="SenderAddress">The guarded address it was sent from, or <see langword="null" /> where it named none.</param>
/// <param name="SentAt">When it was sent, where it said.</param>
/// <param name="Provenance">Where it came from as this deployment recorded it, which the reading states as attributes the message cannot write.</param>
/// <param name="Subject">The guarded subject, or <see langword="null" /> where it carried none.</param>
/// <param name="Body">The guarded body, already cut to what a reading shows.</param>
/// <param name="BodyCut">Whether the body was cut, which the reading states.</param>
internal sealed record GuardedThreadReading(
    StoredEmailId Id,
    PresentationCitationId Source,
    string? SenderAddress,
    DateTimeOffset? SentAt,
    EmailProvenance Provenance,
    string? Subject,
    string Body,
    bool BodyCut);

/// <summary>One calendar event as the Agent is shown it, its title already guarded.</summary>
/// <param name="Start">When it begins.</param>
/// <param name="End">When it ends, where it states an end.</param>
/// <param name="IsAllDay">Whether it is a whole day rather than a clock time.</param>
/// <param name="Title">The guarded title.</param>
internal sealed record GuardedCalendarReading(DateTimeOffset Start, DateTimeOffset? End, bool IsAllDay, string Title);

/// <summary>One task as the Agent is shown it, its title already guarded.</summary>
/// <param name="IsCompleted">Whether it is done.</param>
/// <param name="Origin">Where it came from.</param>
/// <param name="DueOn">The day it is due, where it has one.</param>
/// <param name="Title">The guarded title.</param>
internal sealed record GuardedTaskReading(bool IsCompleted, PersonalTaskOrigin Origin, DateOnly? DueOn, string Title);
