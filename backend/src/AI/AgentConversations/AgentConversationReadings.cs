// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using MailFathom.AI.Orchestration;
using MailFathom.Application.Discovery.Presentation.Citations;
using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Census;
using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.Application.Emails.Provenance;
using MailFathom.Domain.Delivery.Receipts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Tasks;

namespace MailFathom.AI.AgentConversations;

/// <summary>Writes what the Agent's reading tools return: a conversation, a page of a message or of a file it carries, a page of the mailbox, a stretch of the calendar, and the task list.</summary>
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

    /// <summary>Names the attribute carrying how many characters a message's whole body holds.</summary>
    internal const string BodyCharactersAttributeName = "body-characters";

    /// <summary>Names the attribute carrying how many of those characters a conversation reading shows.</summary>
    internal const string BodyShownAttributeName = "body-shown";

    /// <summary>Names the element one file a message carries is described in.</summary>
    internal const string AttachedFileElementName = "attached-file";

    /// <summary>Names the attribute carrying the position a file is named by, which reading its words takes.</summary>
    internal const string AttachmentAttributeName = "attachment";

    /// <summary>Names the attribute carrying the name a file's sender gave it.</summary>
    internal const string FileAttributeName = "file";

    /// <summary>Names the attribute carrying the media type a file declares, which is its sender's claim.</summary>
    internal const string MediaTypeAttributeName = "type";

    /// <summary>Names the attribute carrying a file's decoded size in octets.</summary>
    internal const string SizeAttributeName = "size";

    /// <summary>Names the attribute carrying how many pages, slides, or sheets a file's words were read into.</summary>
    internal const string PagesAttributeName = "pages";

    /// <summary>Names the attribute carrying which of those three a file's places are.</summary>
    internal const string PageKindAttributeName = "page-kind";

    /// <summary>Names the attribute carrying how many characters a body or a file's words hold in all.</summary>
    internal const string CharactersAttributeName = "characters";

    /// <summary>Names the attribute saying whose words a file's text is.</summary>
    internal const string ReadAsAttributeName = "read-as";

    /// <summary>The value <see cref="ReadAsAttributeName" /> carries for words the file itself holds, a scanned page's transcription among them.</summary>
    internal const string DocumentReadAs = "document";

    /// <summary>The value <see cref="ReadAsAttributeName" /> carries for a model's account of a picture, which nobody wrote.</summary>
    internal const string PictureDescriptionReadAs = "picture-description";

    /// <summary>Names the attribute saying why nothing of a file can be read, written only where nothing can.</summary>
    internal const string UnreadAttributeName = "unread";

    /// <summary>The value <see cref="UnreadAttributeName" /> carries for a file no reading has been stored for.</summary>
    internal const string NeverReadReason = "never-read";

    /// <summary>Names the element a page of one message's body is read into.</summary>
    internal const string MessagePageElementName = "message-page";

    /// <summary>Names the element a page of one file's words is read into.</summary>
    internal const string AttachmentPageElementName = "attachment-page";

    /// <summary>Names the element saying a file has no words to read, and why.</summary>
    internal const string AttachmentUnreadElementName = "attachment-unread";

    /// <summary>Names the element one place of a file a page reaches is quoted in.</summary>
    internal const string PartElementName = "part";

    /// <summary>Names the attribute carrying where a page begins, counted in characters from the start of the whole.</summary>
    internal const string PageStartAttributeName = "start";

    /// <summary>Names the attribute carrying where a page ends, counted the same way.</summary>
    internal const string PageEndAttributeName = "end";

    /// <summary>Names the attribute carrying where the next page begins, written only where something remains.</summary>
    internal const string NextAttributeName = "next";

    /// <summary>Names the attribute carrying which kind of place a part was read from.</summary>
    internal const string SegmentAttributeName = "segment";

    /// <summary>Names the attribute carrying that place's one-based number.</summary>
    internal const string SegmentNumberAttributeName = "segment-number";

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

    /// <summary>Names the element one census reading occupies.</summary>
    internal const string CensusElementName = "census";

    /// <summary>Names the attribute carrying what the census grouped the mail by.</summary>
    internal const string GroupedByAttributeName = "grouped-by";

    /// <summary>Names the attribute carrying how many messages the scope and the filters hold.</summary>
    internal const string TotalAttributeName = "total";

    /// <summary>Names the attribute carrying how many groups the mail falls into in all.</summary>
    internal const string GroupsAttributeName = "groups";

    /// <summary>Names the attribute carrying how many messages every group counts between them.</summary>
    internal const string CountedInGroupsAttributeName = "counted-in-groups";

    /// <summary>Names the attribute carrying how many groups lie beyond the ones shown.</summary>
    internal const string GroupsBeyondAttributeName = "groups-beyond";

    /// <summary>Names the attribute carrying how many messages the groups beyond the ones shown count.</summary>
    internal const string MessagesBeyondAttributeName = "messages-beyond";

    /// <summary>Names the element one group of a census occupies.</summary>
    internal const string GroupElementName = "group";

    /// <summary>Names the attribute carrying what a group is keyed by.</summary>
    internal const string KeyAttributeName = "key";

    /// <summary>Names the attribute marking the group of mail that carries no key: no sender address, or no arrival date.</summary>
    internal const string NoneAttributeName = "none";

    /// <summary>Names the attribute carrying how many messages a group counts.</summary>
    internal const string CountAttributeName = "count";

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
            WriteOptional(document, BodyCharactersAttributeName, message.BodyCharacters?.ToString(CultureInfo.InvariantCulture));
            WriteOptional(document, BodyShownAttributeName, message.BodyCharacters is null ? null : message.BodyShown.ToString(CultureInfo.InvariantCulture));

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

            foreach (var attachment in message.Attachments ?? [])
            {
                WriteAttachedFile(document, attachment);
            }

            document.EndElement();
        }

        return document.Complete();
    }

    /// <summary>Writes one page of a message's body, saying what part of the whole it is and where the next begins.</summary>
    /// <param name="page">The page, its text already guarded.</param>
    /// <returns>The document.</returns>
    /// <remarks>
    /// The offsets are the service's own counts rather than anything the page says, so a body cannot claim to have
    /// ended where it has not; and the next page's start is written only where something remains, so its absence is
    /// what says the body has been read to its end.
    /// </remarks>
    internal static string MessagePage(GuardedMessagePage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        using var document = new AgentTurnDocument();

        document.StartElement(MessagePageElementName);
        document.WriteAttribute(MessageIdAttributeName, page.Id.ToString());
        document.WriteAttribute(SourceAttributeName, page.Source.Value);
        WritePageBounds(document, page.Bounds);
        document.WriteQuotedContent(page.Text, page.Bounds.End < page.Bounds.Characters);
        document.EndElement();

        return document.Complete();
    }

    /// <summary>Writes one page of a file's words, each part of it under the page, slide, or sheet it was read from.</summary>
    /// <param name="page">The page, every text of it already guarded.</param>
    /// <returns>The document.</returns>
    /// <remarks>
    /// The places are attributes and the words are quoted inside them, so nothing a sender put in a document can claim
    /// to be page one of it, and a model's account of a picture is marked as one on the element rather than left for
    /// the words to say.
    /// </remarks>
    internal static string AttachmentPage(GuardedAttachmentPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        using var document = new AgentTurnDocument();

        document.StartElement(AttachmentPageElementName);
        document.WriteAttribute(MessageIdAttributeName, page.Id.ToString());
        document.WriteAttribute(SourceAttributeName, page.Source.Value);
        document.WriteAttribute(AttachmentAttributeName, page.AttachmentPosition.ToString(CultureInfo.InvariantCulture));
        WriteOptional(document, FileAttributeName, page.FileName);
        document.WriteAttribute(ReadAsAttributeName, ReadAs(page.Kind));
        WritePlaces(document, page.PageCount, page.PageKind);
        WritePageBounds(document, page.Bounds);

        foreach (var part in page.Parts)
        {
            document.StartElement(PartElementName);

            if (part.Segment is { } segment)
            {
                document.WriteAttribute(SegmentAttributeName, segment.Kind.ToString());
                document.WriteAttribute(SegmentNumberAttributeName, segment.Number.ToString(CultureInfo.InvariantCulture));
            }

            document.WriteQuotedContent(part.Text);
            document.EndElement();
        }

        document.EndElement();

        return document.Complete();
    }

    /// <summary>Writes that a file has no words to read, and the reason its reading recorded.</summary>
    /// <param name="id">The message carrying the file.</param>
    /// <param name="source">The source this run declared the message as.</param>
    /// <param name="attachment">The file, its name already guarded.</param>
    /// <returns>The document.</returns>
    internal static string AttachmentUnread(StoredEmailId id, PresentationCitationId source, GuardedAttachmentReading attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        using var document = new AgentTurnDocument();

        document.StartElement(AttachmentUnreadElementName);
        document.WriteAttribute(MessageIdAttributeName, id.ToString());
        document.WriteAttribute(SourceAttributeName, source.Value);
        WriteFileIdentity(document, attachment);
        document.WriteAttribute(UnreadAttributeName, UnreadReason(attachment.Reading));
        document.EndElement();

        return document.Complete();
    }

    /// <summary>Writes one file a message carries: what it is, and how much of it was read or why nothing was.</summary>
    /// <remarks>
    /// Written for every file whether or not anything read it, because a file passed over in silence reads as a message
    /// that carried none, and an answer about a contract nobody could open would then be given from the covering note
    /// as though that were all there was.
    /// </remarks>
    private static void WriteAttachedFile(AgentTurnDocument document, GuardedAttachmentReading attachment)
    {
        document.StartElement(AttachedFileElementName);
        WriteFileIdentity(document, attachment);

        if (attachment.Reading is { WasRead: true } reading)
        {
            document.WriteAttribute(ReadAsAttributeName, ReadAs(reading.Kind));
            WritePlaces(document, reading.PageCount, reading.PageKind);
            document.WriteAttribute(CharactersAttributeName, reading.TextLength.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            document.WriteAttribute(UnreadAttributeName, UnreadReason(attachment.Reading));
        }

        document.EndElement();
    }

    private static void WriteFileIdentity(AgentTurnDocument document, GuardedAttachmentReading attachment)
    {
        document.WriteAttribute(AttachmentAttributeName, attachment.Position.ToString(CultureInfo.InvariantCulture));
        WriteOptional(document, FileAttributeName, attachment.FileName);
        document.WriteAttribute(MediaTypeAttributeName, attachment.MediaType);
        document.WriteAttribute(SizeAttributeName, attachment.SizeOctets.ToString(CultureInfo.InvariantCulture));
    }

    private static void WritePlaces(AgentTurnDocument document, int pageCount, AttachmentTextSegmentKind? pageKind)
    {
        if (pageCount > 0)
        {
            document.WriteAttribute(PagesAttributeName, pageCount.ToString(CultureInfo.InvariantCulture));
        }

        WriteOptional(document, PageKindAttributeName, pageKind?.ToString());
    }

    private static void WritePageBounds(AgentTurnDocument document, PageBounds bounds)
    {
        document.WriteAttribute(CharactersAttributeName, bounds.Characters.ToString(CultureInfo.InvariantCulture));
        document.WriteAttribute(PageStartAttributeName, bounds.Start.ToString(CultureInfo.InvariantCulture));
        document.WriteAttribute(PageEndAttributeName, bounds.End.ToString(CultureInfo.InvariantCulture));
        WriteOptional(document, NextAttributeName, bounds.End < bounds.Characters ? bounds.End.ToString(CultureInfo.InvariantCulture) : null);
        WriteTrue(document, CeilingReachedAttributeName, bounds.CeilingReached);
    }

    private static string ReadAs(AttachmentTextKind kind) =>
        kind is AttachmentTextKind.ImageDescription ? PictureDescriptionReadAs : DocumentReadAs;

    /// <summary>Names why a file has no words: the outcome its reading recorded, or that no reading was ever stored.</summary>
    private static string UnreadReason(AttachmentTextReading? reading) =>
        string.IsNullOrWhiteSpace(reading?.Outcome) ? NeverReadReason : reading.Outcome;

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

    /// <summary>Writes one census reading: the total, and the leading groups with how much lies beyond them.</summary>
    /// <param name="dimension">What the mail was grouped by.</param>
    /// <param name="census">The census, its groups the ones the run may show and every key among them already guarded.</param>
    /// <param name="ceilingReached">Whether groups were left out because the run may read no more.</param>
    /// <returns>The document.</returns>
    /// <remarks>
    /// How much lies beyond the groups shown is written on the reading, because a model handed twenty senders with nothing
    /// saying more exist names them as everybody who writes. A group carries no message identifier, so nothing in a
    /// census can become a citation.
    /// </remarks>
    internal static string Census(MailCensusDimension dimension, MailCensus census, bool ceilingReached)
    {
        ArgumentNullException.ThrowIfNull(census);

        using var document = new AgentTurnDocument();

        document.StartElement(CensusElementName);
        document.WriteAttribute(TotalAttributeName, census.Total.ToString(CultureInfo.InvariantCulture));

        if (dimension is not MailCensusDimension.Total)
        {
            document.WriteAttribute(GroupedByAttributeName, GroupedBy(dimension));
            document.WriteAttribute(GroupsAttributeName, census.GroupCount.ToString(CultureInfo.InvariantCulture));
            document.WriteAttribute(CountedInGroupsAttributeName, census.CountedInGroups.ToString(CultureInfo.InvariantCulture));
            WriteOptional(document, GroupsBeyondAttributeName, Count(census.GroupsBeyond));
            WriteOptional(document, MessagesBeyondAttributeName, Count(census.CountedBeyond));
            WriteTrue(document, CeilingReachedAttributeName, ceilingReached);
        }

        foreach (var group in census.Groups)
        {
            document.StartElement(GroupElementName);
            WriteOptional(document, KeyAttributeName, group.Key);
            WriteTrue(document, NoneAttributeName, group.Key is null);
            document.WriteAttribute(CountAttributeName, group.Count.ToString(CultureInfo.InvariantCulture));
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

    private static string GroupedBy(MailCensusDimension dimension) => dimension switch
    {
        MailCensusDimension.SenderAddress => "sender",
        MailCensusDimension.SenderDomain => "domain",
        MailCensusDimension.Day => "day",
        MailCensusDimension.Week => "week",
        MailCensusDimension.Month => "month",
        MailCensusDimension.Category => "category",
        _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, "A total is grouped by nothing."),
    };

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
/// <param name="BodyCharacters">How many characters its whole body holds, or <see langword="null" /> where the reading states no length.</param>
/// <param name="BodyShown">How many of those characters the reading shows.</param>
/// <param name="Attachments">Every file it carries, each with what reading it came to, or <see langword="null" /> where the reading names none.</param>
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
    bool AnsweredWithFooter = false,
    int? BodyCharacters = null,
    int BodyShown = 0,
    IReadOnlyList<GuardedAttachmentReading>? Attachments = null);

/// <summary>One file a message carries as the Agent is shown it, its name already guarded.</summary>
/// <param name="Position">The position the file is named by, which reading its words takes.</param>
/// <param name="FileName">The guarded name its sender gave it, or <see langword="null" /> where it carried none.</param>
/// <param name="MediaType">The media type it declares.</param>
/// <param name="SizeOctets">Its decoded size.</param>
/// <param name="Reading">What reading it came to, or <see langword="null" /> where nothing has read it.</param>
internal sealed record GuardedAttachmentReading(
    int Position,
    string? FileName,
    string MediaType,
    long SizeOctets,
    AttachmentTextReading? Reading);

/// <summary>Where a page of a body or a file lies in the whole, counted by the service in characters.</summary>
/// <param name="Characters">How many characters the whole holds.</param>
/// <param name="Start">Where the page begins.</param>
/// <param name="End">Where it ends, which is where the next page begins.</param>
/// <param name="CeilingReached">Whether the run's ceiling on retrieved mail cut the page short.</param>
internal sealed record PageBounds(int Characters, int Start, int End, bool CeilingReached);

/// <summary>One page of a message's body as the Agent is shown it, its text already guarded.</summary>
/// <param name="Id">The message's identifier.</param>
/// <param name="Source">The source this run declared it as, which an answer cites.</param>
/// <param name="Bounds">Where the page lies in the whole body.</param>
/// <param name="Text">The guarded page.</param>
internal sealed record GuardedMessagePage(StoredEmailId Id, PresentationCitationId Source, PageBounds Bounds, string Text);

/// <summary>One page of a file's words as the Agent is shown it, every text of it already guarded.</summary>
/// <param name="Id">The message carrying the file.</param>
/// <param name="Source">The source this run declared the message as, which an answer cites.</param>
/// <param name="AttachmentPosition">The position the file is named by.</param>
/// <param name="FileName">The guarded name its sender gave it, or <see langword="null" /> where it carried none.</param>
/// <param name="Kind">Whether the words are the file's own or a model's account of a picture.</param>
/// <param name="PageCount">How many pages, slides, or sheets the whole was read into.</param>
/// <param name="PageKind">Which of the three those are, or <see langword="null" /> where the reading recorded none.</param>
/// <param name="Bounds">Where the page lies in the file's whole text.</param>
/// <param name="Parts">The page cut at the places it crosses, in reading order.</param>
internal sealed record GuardedAttachmentPage(
    StoredEmailId Id,
    PresentationCitationId Source,
    int AttachmentPosition,
    string? FileName,
    AttachmentTextKind Kind,
    int PageCount,
    AttachmentTextSegmentKind? PageKind,
    PageBounds Bounds,
    IReadOnlyList<GuardedAttachmentPart> Parts);

/// <summary>The part of a page one place of a file contributed, its text already guarded.</summary>
/// <param name="Segment">The page, slide, or sheet it was read from, or <see langword="null" /> where the reading recorded none.</param>
/// <param name="Text">The guarded part.</param>
internal sealed record GuardedAttachmentPart(AttachmentTextSegment? Segment, string Text);

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
