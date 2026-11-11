// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using MailFathom.AI.Orchestration;
using MailFathom.Application.Discovery.Presentation.Citations;
using MailFathom.Application.Emails.Provenance;
using MailFathom.Domain.Delivery.Receipts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Tasks;

namespace MailFathom.AI.AgentConversations;

/// <summary>Writes what the Agent's reading tools return: a conversation, a page of the mailbox, a stretch of the calendar, and the task list.</summary>
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

    /// <summary>Names the attribute carrying the identifier of the conversation itself, which a reading may be repeated with to reach its newest messages.</summary>
    internal const string ThreadIdAttributeName = "id";

    /// <summary>Names the attribute counting the conversation's messages before the first one shown, written only where there are some.</summary>
    internal const string EarlierLeftOutAttributeName = "earlier-messages-left-out";

    /// <summary>Names the attribute counting the conversation's messages after the last one shown, written only where there are some.</summary>
    internal const string LaterLeftOutAttributeName = "later-messages-left-out";

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

    /// <summary>Names the attribute marking a message held in an account that closes what the person sends from it with their footer.</summary>
    internal const string FooterAttributeName = "footer";

    /// <summary>Names the element carrying a message's subject.</summary>
    internal const string SubjectElementName = "subject";

    /// <summary>Names the element carrying a message's body.</summary>
    internal const string BodyElementName = "body";

    /// <summary>Names the element a page of the mailbox's timeline is read into.</summary>
    internal const string MailboxElementName = "mailbox";

    /// <summary>Names the attribute carrying which end of the timeline a page was read from.</summary>
    internal const string OrderAttributeName = "order";

    /// <summary>The value <see cref="OrderAttributeName" /> carries for a page read from the newest mail back.</summary>
    internal const string NewestFirstOrder = "newest-first";

    /// <summary>The value <see cref="OrderAttributeName" /> carries for a page read from the oldest mail on.</summary>
    internal const string OldestFirstOrder = "oldest-first";

    /// <summary>Names the attribute saying more mail matches than the page shows, written only where it does.</summary>
    internal const string MoreAttributeName = "more";

    /// <summary>Names the attribute carrying what the next page continues from, written only where one can be read.</summary>
    internal const string CursorAttributeName = "cursor";

    /// <summary>Names the attribute saying the run may read no more mail, written only where the page was cut by that ceiling.</summary>
    internal const string CeilingReachedAttributeName = "ceiling-reached";

    /// <summary>Names the attribute carrying when a message arrived.</summary>
    internal const string ReceivedAttributeName = "received";

    /// <summary>Names the attribute carrying whether the mailbox has opened a message.</summary>
    internal const string ReadAttributeName = "read";

    /// <summary>Names the attribute marking a flagged message, written only where it is.</summary>
    internal const string FlaggedAttributeName = "flagged";

    /// <summary>Names the attribute marking a message carrying attachments, written only where it does.</summary>
    internal const string AttachmentsAttributeName = "attachments";

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

    /// <summary>Writes one conversation, or the stretch of it a reading shows.</summary>
    /// <param name="messages">The messages shown, in the conversation's order.</param>
    /// <param name="threadId">The conversation, or <see langword="null" /> where the messages belong to none.</param>
    /// <param name="earlierLeftOut">How many of the conversation's messages come before the first one shown.</param>
    /// <param name="laterLeftOut">How many of the conversation's messages come after the last one shown.</param>
    /// <returns>The document.</returns>
    /// <remarks>
    /// What was left out is counted on the conversation rather than hidden, because a model shown part of a long
    /// conversation as though it were the whole of it answers about the part with the confidence of the whole.
    /// </remarks>
    internal static string Thread(
        IReadOnlyList<GuardedThreadReading> messages,
        EmailThreadId? threadId = null,
        int earlierLeftOut = 0,
        int laterLeftOut = 0)
    {
        ArgumentNullException.ThrowIfNull(messages);

        using var document = new AgentTurnDocument();

        document.StartElement(ThreadElementName);
        WriteOptional(document, ThreadIdAttributeName, threadId?.ToString());
        WriteOptional(document, EarlierLeftOutAttributeName, Count(earlierLeftOut));
        WriteOptional(document, LaterLeftOutAttributeName, Count(laterLeftOut));

        foreach (var message in messages)
        {
            document.StartElement(MessageElementName);
            document.WriteAttribute(MessageIdAttributeName, message.Id.ToString());
            document.WriteAttribute(SourceAttributeName, message.Source.Value);
            WriteOptional(document, FromAttributeName, message.SenderAddress);
            WriteOptional(document, SentAttributeName, Instant(message.SentAt));
            document.WriteProvenance(message.Provenance);

            if (message.AnsweredWithFooter)
            {
                document.WriteAttribute(FooterAttributeName, "true");
            }

            AgentReceiptReadings.Write(document, message.Receipts);

            if (message.Subject is { } subject)
            {
                document.WriteQuoted(SubjectElementName, subject);
            }

            document.WriteQuoted(BodyElementName, message.Body, message.BodyCut);
            document.EndElement();
        }

        return document.Complete();
    }

    /// <summary>Writes one page of the mailbox's timeline: each message's headers and state, and nothing of its body.</summary>
    /// <param name="messages">The messages, in the order the page was read in.</param>
    /// <param name="newestFirst">Whether the page was read from the newest mail back rather than from the oldest on.</param>
    /// <param name="hasMore">Whether more mail matches than the page shows.</param>
    /// <param name="cursor">What the next page continues from, or <see langword="null" /> where none can be read.</param>
    /// <param name="ceilingReached">Whether the page was cut because the run may read no more mail.</param>
    /// <returns>The document.</returns>
    /// <remarks>
    /// That more mail exists is written on the page rather than left to the cursor, because a model handed a page with
    /// nothing saying it is one answers about the page as though it were the mailbox.
    /// </remarks>
    internal static string Listing(
        IReadOnlyList<GuardedListedReading> messages,
        bool newestFirst,
        bool hasMore,
        string? cursor,
        bool ceilingReached)
    {
        ArgumentNullException.ThrowIfNull(messages);

        using var document = new AgentTurnDocument();

        document.StartElement(MailboxElementName);
        document.WriteAttribute(OrderAttributeName, newestFirst ? NewestFirstOrder : OldestFirstOrder);
        WriteTrue(document, MoreAttributeName, hasMore);
        WriteOptional(document, CursorAttributeName, cursor);
        WriteTrue(document, CeilingReachedAttributeName, ceilingReached);

        foreach (var message in messages)
        {
            document.StartElement(MessageElementName);
            document.WriteAttribute(MessageIdAttributeName, message.Id.ToString());
            document.WriteAuthor(message.SenderName, message.SenderAddress);
            WriteOptional(document, ReceivedAttributeName, Instant(message.ReceivedAt));
            document.WriteAttribute(ReadAttributeName, message.IsRead ? "true" : "false");
            WriteTrue(document, FlaggedAttributeName, message.IsFlagged);
            WriteTrue(document, AttachmentsAttributeName, message.HasAttachments);
            document.WriteProvenance(message.Provenance);

            if (message.Subject is { } subject)
            {
                document.WriteQuoted(SubjectElementName, subject);
            }

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

    /// <summary>Writes a count only where it counts something, so a conversation shown whole carries no zero to read.</summary>
    private static string? Count(int count) =>
        count > 0 ? count.ToString(CultureInfo.InvariantCulture) : null;

    /// <summary>Writes an attribute only where there is a value, so the model reads what a record carries rather than blanks to tell apart from it.</summary>
    private static void WriteOptional(AgentTurnDocument document, string attributeName, string? value)
    {
        if (value is not null)
        {
            document.WriteAttribute(attributeName, value);
        }
    }

    /// <summary>Writes a mark only where it holds, so the model reads what a record is rather than a false to tell apart from it.</summary>
    private static void WriteTrue(AgentTurnDocument document, string attributeName, bool holds)
    {
        if (holds)
        {
            document.WriteAttribute(attributeName, "true");
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
/// <param name="Receipts">What the message asked to be told and what its recipients' reports said, every address guarded, where it is one the account sent.</param>
/// <param name="AnsweredWithFooter">Whether the account holding it appends the person's footer, so an answer to it closes with that footer.</param>
internal sealed record GuardedThreadReading(
    StoredEmailId Id,
    PresentationCitationId Source,
    string? SenderAddress,
    DateTimeOffset? SentAt,
    EmailProvenance Provenance,
    string? Subject,
    string Body,
    bool BodyCut,
    OutgoingEmailReceipts? Receipts = null,
    bool AnsweredWithFooter = false);

/// <summary>One message of a page of the mailbox as the Agent is shown it, every text of it already guarded.</summary>
/// <param name="Id">The message's identifier, which a conversation reading and a proposal name it by.</param>
/// <param name="SenderName">The guarded name its sender goes by, or <see langword="null" /> where it named none.</param>
/// <param name="SenderAddress">The guarded address it was sent from, or <see langword="null" /> where it named none.</param>
/// <param name="ReceivedAt">When it arrived, where that is known.</param>
/// <param name="IsRead">Whether the mailbox has opened it.</param>
/// <param name="IsFlagged">Whether it is flagged.</param>
/// <param name="HasAttachments">Whether it carries attachments.</param>
/// <param name="Provenance">Where it came from as this deployment recorded it, which the reading states as attributes the message cannot write.</param>
/// <param name="Subject">The guarded subject, or <see langword="null" /> where it carried none.</param>
internal sealed record GuardedListedReading(
    StoredEmailId Id,
    string? SenderName,
    string? SenderAddress,
    DateTimeOffset? ReceivedAt,
    bool IsRead,
    bool IsFlagged,
    bool HasAttachments,
    EmailProvenance Provenance,
    string? Subject);

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
