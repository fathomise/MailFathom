// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using MailFathom.AI.Orchestration;
using MailFathom.AI.Retrieval;
using MailFathom.Application.Agent.Answering;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Agent.Files;
using MailFathom.Application.Agent.Reports;
using MailFathom.Application.Answers;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Blocks;
using MailFathom.Application.Discovery.Presentation.Citations;
using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Census;
using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Application.Emails.ListEmails;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Provenance;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Emails.Threads;
using MailFathom.Application.Emails.ThreadStates;
using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Authoring;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Calendar;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Tasks;
using Microsoft.Extensions.AI;

namespace MailFathom.AI.AgentConversations;

/// <summary>What one Agent run may do besides answer: read the person's mail and the files it carries, calendar, and tasks, and propose what to do about them.</summary>
/// <remarks>
/// <para>
/// <strong>The tool set is the capability, and it splits in two.</strong> A reading tool answers on the model's own turn,
/// because it discloses nothing the person could not open themselves and costs nothing beyond the run. An act tool never
/// acts: it writes a proposal into the conversation and tells the model so, and only the person accepting that proposal
/// carries it out — through <see cref="AgentProposalAcceptance" />, with exactly the act written here. There is no tool
/// that sends, saves, schedules, or completes anything, so no instruction and no retrieved message can make a run do so.
/// The one tool outside both halves notes what the person might ask next, which the answer's ending carries as text:
/// pressing a suggestion posts it as a question, so it reaches nothing a question could not.
/// </para>
/// <para>
/// <strong>Each tool reaches only what the person's own grant reaches.</strong> Every read goes through the use case the
/// client's own routes use, so the scope, the permissions, and the egress guard are the ones a person opening the same
/// screen meets; a tool whose grant the person lacks is not offered at all rather than offered and refused.
/// </para>
/// <para>
/// <strong>Citations are minted here, never by the model.</strong> Every message a tool shows is declared as a source the
/// moment it is shown, so what the answer rests on is what the run actually read, and nothing a model writes becomes a
/// citation target.
/// </para>
/// <para>
/// <strong>A derivation already made is shown as it stands.</strong> The state of a conversation is written into the
/// conversation as the block the mail screen draws, in the language it was derived in, rather than handed to the model to
/// restate — so the same thread reads the same way in both places.
/// </para>
/// </remarks>
internal sealed partial class AgentConversationTools
{
    /// <summary>The most messages of one conversation a thread reading shows the model.</summary>
    internal const int MaximumThreadMessages = 20;

    /// <summary>The most characters of one message's body a thread reading shows the model.</summary>
    internal const int MaximumBodyCharacters = 4_000;

    /// <summary>The most characters of a message's body or of a file's words one page shows the model.</summary>
    /// <remarks>The same stride a conversation reading cuts a body at, so reading on from that cut takes the same steps again.</remarks>
    internal const int MaximumPageCharacters = MaximumBodyCharacters;

    /// <summary>The most calendar events or tasks one reading shows the model.</summary>
    internal const int MaximumAgendaItems = 50;

    /// <summary>The longest window of the calendar one reading covers.</summary>
    internal static readonly TimeSpan MaximumCalendarWindow = TimeSpan.FromDays(62);

    /// <summary>Names the tool that reads the mailbox in the order mail arrived.</summary>
    internal const string ListMailToolName = "list_mail";

    /// <summary>What the model is told the listing does, and when it is the tool to reach for rather than the search.</summary>
    private const string ListMailDescription =
        "Lists the mailbox in the order mail arrived, newest first unless you ask for oldest first, one page at a time: "
        + "each message's id, sender, when it arrived, subject, and whether it is read, flagged, and carries attachments, "
        + "and nothing of its body. Use it when the question is about the timeline rather than a subject — the latest "
        + "mail, what came in during a period, what is unread or flagged, everything from one sender — and search_mail "
        + "to find mail about a subject. Narrow with the filters for what the question states, as you would a search. "
        + "A page whose mailbox element says more=\"true\" is not the whole of what matches: call again with its cursor "
        + "and the same filters to read the next page. Read a listed message's body with read_thread.";

    /// <summary>Names the tool that counts the mailbox, in total or grouped.</summary>
    internal const string CountMailToolName = "count_mail";

    /// <summary>What the model is told the count does, what one reading carries, and when it is the tool to reach for.</summary>
    private static readonly string CountMailDescription =
        "Counts the mailbox exactly, in the database, without reading any message: how many messages match the filters, "
        + "and, when you group them, how many each sender address, each sender domain, each day, week, or month of arrival "
        + "on the person's clock, or each category counts. Use it for any question about how many or how often, or about which "
        + "senders, organisations, periods, or categories the mail falls into — how many invoices came in October, who "
        + "writes most, which organisations the person corresponds with — rather than estimating from what a search or a "
        + "listing returned, which is never the whole mailbox. It takes the filters "
        + "list_mail takes. "
        + string.Create(
            CultureInfo.InvariantCulture,
            $"A reading leads with at most {MailboxCensusReader.MaximumLeadingGroups} groups, {MailboxCensusReader.DefaultLeadingGroups} unless you ask for another number: ")
        + "senders, domains, and categories by how many they count, the largest first, and periods by how recent they are, "
        + "the latest first. It says how many groups and how many messages lie beyond the ones it shows, and the total; a "
        + "group marked none=\"true\" counts the mail carrying no sender address or no arrival date. A message sorted into "
        + "several categories counts in each, so counted-in-groups may differ from the total. "
        + "Each sender or domain it names spends the same reading allowance a search does. A count cites no message: to "
        + "quote or cite mail, search or list it.";

    /// <summary>Names the tool that reads on in one message's body.</summary>
    internal const string ReadMessageToolName = "read_message";

    /// <summary>Names the tool that reads one file a message carries.</summary>
    internal const string ReadAttachmentToolName = "read_attachment";

    /// <summary>Names the tool that reads a conversation.</summary>
    internal const string ReadThreadToolName = "read_thread";

    /// <summary>Names the tool that hands the person one file a message carries.</summary>
    internal const string OfferAttachmentToolName = "offer_attachment";

    /// <summary>What the model is told once the run's ceiling on retrieved mail leaves nothing for another page.</summary>
    private const string CeilingReachedStatement =
        "This run may read no more mail: its ceiling on retrieved text is spent. Answer from what you have read, and say "
        + "how much of the message or the file that covers.";

    /// <summary>What both proposing tools say of the send time they take, which only a person's own ask may supply.</summary>
    private const string SendAtDescription =
        "When it is to leave, as an ISO 8601 instant with an offset, in the person's time zone the turn names. Give one only "
        + "where the person asked in this conversation for the message to leave later; otherwise leave it empty, and it "
        + "leaves as soon as they accept.";

    /// <summary>What the model is told the conversation reading shows, including how much of each message and which files.</summary>
    private static readonly string ReadThreadDescription =
        $"Reads the email conversation a message belongs to, up to its {MaximumThreadMessages} newest messages: each one's "
        + $"sender, date, subject, and body, the body cut at {MaximumBodyCharacters} characters, with how long the whole "
        + "body is beside how much is shown, and every file the message carries — its position, name, type, size, and how "
        + "many pages, slides, or sheets its words were read into, or why nothing of it could be read. Pass the id of any "
        + "message in it, or of the conversation; where the message is older than those, the reading ends at it instead. "
        + "The reading says how many earlier or later messages it left out. A conversation past "
        + $"{IEmailThreadReader.MaximumAssembledEmails} messages is read from its first {IEmailThreadReader.MaximumAssembledEmails} only.";

    /// <summary>What the model is told reading on in a body does, and when to reach for it.</summary>
    private static readonly string ReadMessageDescription =
        $"Reads one message's body a page of at most {MaximumPageCharacters} characters at a time, from the place you "
        + "name. Use it when a conversation reading or a search showed only part of a body — a body marked cut, or one "
        + "whose body-characters is larger than body-shown — and the answer may lie in the rest. The page says how long "
        + "the whole body is, where the page starts and ends, and, where more remains, the next attribute to pass as from "
        + "to read on. Omit from to start at the beginning.";

    /// <summary>What the model is told reading a file does, and when to reach for it.</summary>
    private static readonly string ReadAttachmentDescription =
        $"Reads the words stored for one file a message carries, a page of at most {MaximumPageCharacters} characters at "
        + "a time, each part of it under the page, slide, or sheet it came from. Name the message and the file's position "
        + "as a conversation reading or a search showed it, and either a page, slide, or sheet number to start there or "
        + "the next attribute of an earlier page to read on; omit both to start at the beginning. The page says how many "
        + "pages, slides, or sheets the file holds and how many characters. Words read-as=\"picture-description\" are a "
        + "model's account of a picture, which nobody wrote. A spreadsheet is read as its rows written out in order, so "
        + "a page of one holds its leading rows rather than anything computed over its columns. A file nothing could "
        + "read answers with the reason instead. To find one clause in a long file, search the message with "
        + $"{ScopedMailKnowledgeRetrieval.MessageSearchToolName} first and read from the page it names.";

    /// <summary>What the model is told offering a file does, which files it may offer, and what the card already says.</summary>
    private const string OfferAttachmentDescription =
        "Places one file a message carries in your answer as a card the person downloads the whole file from, under "
        + "their own access to the message. Offer a file when the person asks for it, or when your answer rests on it. "
        + "Name the message and the file's position exactly as a conversation reading, a search, or read_attachment "
        + "showed them: a file this conversation has not shown you cannot be offered, so read the message's "
        + "conversation first. The card names the file, its type, its size, and the message it came from, so do not "
        + "repeat them. Offering a file says nothing about what it holds.";

    /// <summary>The media type a file is described as where the type its message declared is not one a presentation text holds.</summary>
    private const string GeneralBinaryMediaType = "application/octet-stream";

    /// <summary>Names the tool that writes a report and hands it to the person as a PDF.</summary>
    internal const string WriteReportToolName = "write_report";

    /// <summary>What the model is told writing a report does, what the Markdown may carry, and how a statement cites its source.</summary>
    private static readonly string WriteReportDescription =
        "Writes a report the person asked for — something to forward, file, or print — and places it in your answer as a "
        + "PDF they download. Use it only where the person asked for a report or a document; an ordinary question gets "
        + "an ordinary answer. Read what the report needs first. The service sets the title and when it was written at "
        + "the top and lists the sources at the end, so write neither. "
        + string.Create(CultureInfo.InvariantCulture, $"The title is at most {AgentReportBounds.MaximumTitleCharacters} characters and the body at most {AgentReportBounds.MaximumBodyCharacters} characters of Markdown: ")
        + "headings (## for a section, ### and #### below it), paragraphs, bulleted and numbered lists, tables, "
        + "quotations (>), bold, and italics. A quotation of mail closes with its own line, still inside it, opening "
        + "with \"— \" and naming who wrote it and when. Links, pictures, and HTML are printed as their words only. "
        + "Cite a statement that rests on mail by writing [^id] right after it, with the id of the message as a "
        + "reading, a search, or a "
        + "listing showed it, and one resting on a file the question carries with [^file-id]; a message or a file you "
        + "were not shown is not cited. A run writes one report.";

    /// <summary>The characters a title may carry that a file name may not, which the name the report is saved under replaces.</summary>
    private static readonly char[] FileNameReservedCharacters = ['/', '\\', ':', '*', '?', '"', '<', '>', '|'];

    private readonly AgentAnswerJournal journal;
    private readonly ScopedMailKnowledgeRetrieval retrieval;
    private readonly AgentConversationReaders readers;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly IReadOnlyList<MailAccountId> accounts;
    private readonly AgentConversationFileTools? fileTools;
    private readonly AgentConversationReporting? reporting;
    private readonly Dictionary<StoredEmailId, PresentationCitation> cited = [];
    private readonly Dictionary<StoredEmailId, string?> listed = [];
    private readonly List<string> readBodies = [];
    private readonly HashSet<(StoredEmailId Message, int Position)> shownFiles = [];
    private readonly HashSet<(StoredEmailId Message, int Position)> offeredFiles = [];
    private IReadOnlySet<MailAccountId>? footedAccounts;
    private bool reportWritten;

    /// <summary>Initializes the tools of one run.</summary>
    /// <param name="journal">Where every status, block, and proposal is written the moment it exists.</param>
    /// <param name="retrieval">The mail this run may search, already bound to the person's scope.</param>
    /// <param name="readers">The use cases every tool reads and proposes through, under the person's own grant.</param>
    /// <param name="egressGuard">Scans every text a tool hands the model.</param>
    /// <param name="accounts">The accounts the person reads, which are the ones a new message may be sent from.</param>
    /// <param name="fileTools">The tools over the files the person handed to the conversation, or <see langword="null" /> where the run reads none.</param>
    /// <param name="reporting">Who a report would be written for and in which conversation, or <see langword="null" /> where the run writes none.</param>
    internal AgentConversationTools(
        AgentAnswerJournal journal,
        ScopedMailKnowledgeRetrieval retrieval,
        AgentConversationReaders readers,
        SensitiveContentEgressGuard egressGuard,
        IReadOnlyList<MailAccountId> accounts,
        AgentConversationFileTools? fileTools = null,
        AgentConversationReporting? reporting = null)
    {
        this.journal = journal;
        this.retrieval = retrieval;
        this.readers = readers;
        this.egressGuard = egressGuard;
        this.accounts = accounts;
        this.fileTools = fileTools;
        this.reporting = reporting;
    }

    /// <summary>Gets every source the run has shown the model, in the order it was first shown.</summary>
    internal IReadOnlyList<PresentationCitation> Cited => [.. this.cited.Values];

    /// <summary>Gets the text of every message the run has shown the model, cut where it was cut for the model, which is what an answer's links are traced to.</summary>
    /// <remarks>Every source the run shows is one its answer cites, so this is the cited mail's text: what a search returned, what a thread reading showed, every page of a body or a file it read, and the subjects a listing showed — and beside it every word of the person's own files the run was given or read, which an answer may link out of as well.</remarks>
    internal IEnumerable<string> ShownMail => [.. AnswerMarkup.TextOf(this.retrieval.Report.Passages), .. this.readBodies, .. this.listed.Values.OfType<string>(), .. this.fileTools?.Shown ?? []];

    /// <summary>Gets whether a proposal is on record from this run, which no later model may be asked to answer beside.</summary>
    internal bool HasProposed { get; private set; }

    /// <summary>Gets the questions the model last suggested asking next, which the answer's ending carries.</summary>
    /// <remarks>Held rather than written, because they belong to the answer that completes and a model that suggests and then fails has suggested nothing for the one that follows it.</remarks>
    internal IReadOnlyList<PresentationText> FollowUps { get; private set; } = [];

    /// <summary>Gets the band the model last rated its answer at, or <see langword="null" /> where it rated none.</summary>
    /// <remarks>Held for the same reason as <see cref="FollowUps" />: it describes the answer that completes, so a model that rated and then failed rated nothing for the one that follows it.</remarks>
    internal PresentationConfidence? ReportedConfidence { get; private set; }

    /// <summary>Gets the accounts the model is told of: the person's where a tool it is offered takes one, and none otherwise.</summary>
    /// <remarks>
    /// Which mailboxes somebody has is read under <see cref="MailFathomPermission.MailRead" /> everywhere else, so a
    /// model answering a caller without it is not handed their names as a side effect of being asked something. The
    /// one other reason to name them is the proposal of a new message, which is sent from one.
    /// </remarks>
    internal IReadOnlyList<MailAccountId> NamedAccounts =>
        this.readers.Authorization.Permits(MailFathomPermission.MailRead) || this.ProposesMail ? this.accounts : [];

    /// <summary>Names the accounts the model is told of, each with whether a message sent from it closes with the person's footer.</summary>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The accounts <see cref="NamedAccounts" /> holds, in its order.</returns>
    internal async Task<IReadOnlyList<AgentNamedAccount>> NameAccountsAsync(CancellationToken cancellationToken)
    {
        var footed = await this.FootedAccountsAsync(cancellationToken);

        return [.. this.NamedAccounts.Select(account => new AgentNamedAccount(account, footed.Contains(account)))];
    }

    private bool ProposesMail =>
        this.readers.Authorization.Permits(MailFathomPermission.MailDraftsWrite)
        && this.readers.Authorization.Permits(MailFathomPermission.MailSend);

    /// <summary>Creates the tools the person's grant allows.</summary>
    /// <returns>
    /// Each reading tool where the grant reads that record — mail, the calendar, the task list — each of the two that
    /// propose onto the calendar or the task list where the grant writes it, and the ones proposing mail where it
    /// drafts and sends. The two over the conversation's own files need no grant beyond the one the run was asked
    /// under, being the person's own files of this conversation, and are offered where it holds a document with words.
    /// The one writing a report needs none either, writing nothing but a file of the same conversation, and is offered
    /// wherever the run was handed the conversation to store it in — which every Agent run is.
    /// </returns>
    internal IReadOnlyList<AITool> Create()
    {
        List<AITool> tools =
        [
            AIFunctionFactory.Create(this.SuggestFollowUps, "suggest_follow_ups", $"Suggests up to {AgentConversationBounds.MaximumFollowUps} short questions the person could ask you next, shown under your answer. Each is a question in the person's own voice, never an offer of yours such as \"Would you like…\". Pressing one only asks it; nothing is done."),
            AIFunctionFactory.Create(this.RateConfidence, "rate_confidence", "Rates how settled your answer is, shown beside it: high, moderate, or low."),
            .. this.fileTools?.Create() ?? [],
        ];

        if (this.reporting is not null)
        {
            tools.Add(AIFunctionFactory.Create(this.WriteReportAsync, WriteReportToolName, WriteReportDescription));
        }

        if (this.readers.Authorization.Permits(MailFathomPermission.MailRead))
        {
            tools.Add(new ReportingFunction(this.retrieval.CreateSearchTool(), this.journal, AgentActivity.SearchingMail));
            tools.Add(new ReportingFunction(this.retrieval.CreateMessageSearchTool(), this.journal, AgentActivity.SearchingMail));
            tools.Add(AIFunctionFactory.Create(this.ListMailAsync, ListMailToolName, ListMailDescription));
            tools.Add(AIFunctionFactory.Create(this.CountMailAsync, CountMailToolName, CountMailDescription));
            tools.Add(AIFunctionFactory.Create(this.ReadThreadAsync, ReadThreadToolName, ReadThreadDescription));
            tools.Add(AIFunctionFactory.Create(this.ReadMessageAsync, ReadMessageToolName, ReadMessageDescription));
            tools.Add(AIFunctionFactory.Create(this.ReadAttachmentAsync, ReadAttachmentToolName, ReadAttachmentDescription));
            tools.Add(AIFunctionFactory.Create(this.OfferAttachmentAsync, OfferAttachmentToolName, OfferAttachmentDescription));
            tools.Add(AIFunctionFactory.Create(this.ShowThreadStateAsync, "show_thread_state", "Shows the person where the conversation a message belongs to stands — what was agreed, what is still open, what somebody owes — exactly as it was already derived. Use it only when the person asks where a conversation stands or what is open in it; to read, summarise, or retell a conversation, use read_thread. The reading is placed in the conversation for them; do not restate it."));
        }

        if (this.readers.Authorization.Permits(MailFathomPermission.CalendarRead))
        {
            tools.Add(AIFunctionFactory.Create(this.ReadCalendarAsync, "read_calendar", "Reads the person's calendar between two instants, earliest first."));
        }

        if (this.readers.Authorization.Permits(MailFathomPermission.TasksRead))
        {
            tools.Add(AIFunctionFactory.Create(this.ReadTasksAsync, "read_tasks", "Reads the person's task list, soonest due first, including what their mail proposed as a task."));
        }

        if (this.readers.Authorization.Permits(MailFathomPermission.CalendarWrite))
        {
            tools.Add(AIFunctionFactory.Create(this.ProposeEventAsync, "propose_event", "Proposes a calendar event for the person to review. Nothing is scheduled: the person sees the event, corrects its hour, and decides."));
        }

        if (this.readers.Authorization.Permits(MailFathomPermission.TasksWrite))
        {
            tools.Add(AIFunctionFactory.Create(this.ProposeTaskAsync, "propose_task", "Proposes a task for the person's own list. Nothing is created: the person sees the task, corrects its due day, and decides."));
        }

        if (this.ProposesMail)
        {
            tools.Add(AIFunctionFactory.Create(this.ProposeMessageAsync, "propose_message", "Proposes a new email for the person to review. Nothing is sent: the person sees the draft and decides."));

            if (this.readers.Authorization.Permits(MailFathomPermission.MailRead))
            {
                tools.Add(AIFunctionFactory.Create(this.ProposeReplyAsync, "propose_reply", "Proposes an answer to a message — a reply, a reply to all, or a forward — for the person to review. Nothing is sent: the person sees the draft and decides."));
            }
        }

        return tools;
    }

    /// <summary>Forgets what an earlier model suggested and rated, so the answer carries only what the model that composed it said of it.</summary>
    internal void ForgetEndingReports()
    {
        this.FollowUps = [];
        this.ReportedConfidence = null;
    }

    /// <summary>Declares every message the search showed the model as a source, which is what an answer resting on a search cites, and then the messages a listing showed.</summary>
    /// <param name="cancellationToken">Cancels the writes.</param>
    /// <returns>A task that completes once every source is declared.</returns>
    /// <remarks>
    /// A listed message is declared only while the answer could still cite it. A page holds up to a hundred messages
    /// and an answer cites at most <see cref="PresentationEvidence.MaxCitations" />, which it takes in the order the
    /// sources were declared, so declaring the rest would write sources nothing can ever name.
    /// </remarks>
    internal async Task DeclareSearchedAndListedAsync(CancellationToken cancellationToken)
    {
        foreach (var passage in this.retrieval.Report.Passages.DistinctBy(static passage => passage.StoredEmailId))
        {
            await this.CiteAsync(passage.StoredEmailId, passage.Subject, cancellationToken);
        }

        foreach (var (message, subject) in this.listed.Where(entry => !this.cited.ContainsKey(entry.Key)).ToArray())
        {
            if (this.cited.Count >= PresentationEvidence.MaxCitations)
            {
                break;
            }

            await this.CiteAsync(message, subject, cancellationToken);
        }
    }

    [Description("Suggests what the person could ask next.")]
    private string SuggestFollowUps(
        [Description("Each question as the person would ask it of you, one short line each, never an offer of yours.")] string[] questions)
    {
        if (AgentFollowUps.Read(questions) is not { } suggested)
        {
            return $"Give one to {AgentConversationBounds.MaximumFollowUps} questions, each one line of at most {AgentConversationBounds.MaximumFollowUpLength} characters.";
        }

        this.FollowUps = suggested;

        return "Noted. They are shown under your answer once it is complete, and nothing is asked until the person presses one.";
    }

    [Description("Rates how settled your answer is.")]
    private string RateConfidence(
        [Description("high when the messages you read say it outright and your answer restates them; moderate when your answer takes a step of inference the person may want to check; low when it is your best reading of partial sources and may be wrong.")] string? band)
    {
        PresentationConfidence? rated = band?.Trim().ToUpperInvariant() switch
        {
            "HIGH" => PresentationConfidence.High,
            "MODERATE" => PresentationConfidence.Moderate,
            "LOW" => PresentationConfidence.Low,
            _ => null,
        };

        if (rated is null)
        {
            return "Rate it as high, moderate, or low.";
        }

        this.ReportedConfidence = rated;

        return "Noted. It is shown beside your answer.";
    }

    [Description("Lists mail in the order it arrived.")]
    private async Task<string> ListMailAsync(
        [Description("newestFirst for the latest mail, oldestFirst to start from the earliest. Omit for newestFirst.")] string? direction = null,
        [Description("How many messages one page holds, from 1 to 100. Omit for 25.")] int? pageSize = null,
        [Description("The cursor a previous page's mailbox element carried, to read the page after it with the same filters and direction. Omit for the first page.")] string? cursor = null,
        [Description("Return only mail sent from this whole mail address, without regard to case. Omit to match any sender.")] string? senderAddress = null,
        [Description("Return only mail addressed to this whole mail address in its To or Cc header. Omit to match any recipient.")] string? recipientAddress = null,
        [Description("Return only mail whose subject contains this text, without regard to case. Omit to match any subject.")] string? subjectFragment = null,
        [Description("Return only mail received at or after this moment, written as yyyy-MM-ddTHH:mm against the date and time the turn states, with no zone and no Z. Omit for no lower bound.")] string? receivedOnOrAfter = null,
        [Description("Return only mail received strictly before this moment, written as yyyy-MM-ddTHH:mm against the date and time the turn states, with no zone and no Z. Omit for no upper bound.")] string? receivedBefore = null,
        [Description("Return only mail this mailbox has opened (true) or not opened (false). Mail whose state nobody has observed yet counts as not opened. Listing never changes this state. Omit to match either.")] bool? isMarkedSeen = null,
        [Description("Return only mail flagged (true) or unflagged (false), which is the star most mail clients show. Omit to match either.")] bool? isFlagged = null,
        [Description("Return only mail carrying this keyword, a flag a mail client or server set such as $Junk or a label. Omit to match any keyword.")] string? keyword = null,
        [Description("Return only mail that carries attachments (true) or carries none (false). Omit to match either.")] bool? hasAttachments = null,
        [Description("Return only mail sorted into this category, under the same rule a search's category follows. Omit to match any category.")] string? category = null,
        [Description("Return only mail in the folder playing this role in each account: Inbox, Sent, Archive, Drafts, Trash, Flagged, or Important. Omit to list every folder.")] string? folder = null,
        CancellationToken cancellationToken = default)
    {
        await this.ReportAsync(AgentActivity.ListingMail, cancellationToken);

        if (this.ListingRequestOf(direction, receivedOnOrAfter, receivedBefore, folder, out var request) is { } refusal)
        {
            return refusal;
        }

        request = request with
        {
            PageSize = pageSize,
            Cursor = cursor,
            SenderAddress = senderAddress,
            RecipientAddress = recipientAddress,
            SubjectFragment = subjectFragment,
            IsMarkedSeen = isMarkedSeen,
            IsFlagged = isFlagged,
            Keyword = keyword,
            HasAttachments = hasAttachments,
            Category = category,
        };

        ListEmailsResult page;

        try
        {
            page = await this.readers.Timeline.ListEmailsAsync(request, cancellationToken);
        }
        catch (MailFathomException refused) when (refused is MailboxQueryFilterInvalidException
            or MailboxQueryPageSizeOutOfRangeException
            or MailboxQueryCursorMalformedException
            or MailboxQueryCursorFilterMismatchException)
        {
            // Told rather than raised, because the caller is a tool loop and a model that wrote an unusable value can
            // write a usable one. Each of these messages names the argument and never what was in it.
            return refused.Message;
        }

        var admitted = this.retrieval.RunLedger.AdmitSummaries(page.Emails);
        var ceilingReached = admitted.Count < page.Emails.Count;

        var shown = await this.GuardedListingOfAsync(admitted, cancellationToken);

        // Only what the page shows may be cited, under the subject the model was shown.
        foreach (var reading in shown)
        {
            this.listed.TryAdd(reading.Id, reading.Subject);
        }

        // Once the ceiling cut the page, no cursor is offered: it would continue after the messages the ceiling left
        // out, and the run may read no more mail anyway.
        return AgentConversationReadings.Listing(
            shown,
            request.Direction is EmailTimelineDirection.NewestFirst,
            page.HasMore || ceilingReached,
            ceilingReached ? null : page.NextCursor,
            ceilingReached);
    }

    [Description("Counts mail, in total or grouped.")]
    private async Task<string> CountMailAsync(
        [Description("What to group the mail by: sender (the sender's address), domain (the domain of the sender's address, which is the organisation), day, week, or month (when it arrived, on the person's clock), or category. Omit for one total.")] string? groupBy = null,
        [Description("How many groups the reading shows at most. Omit for the default.")] int? groups = null,
        [Description("Count only mail sent from this whole mail address, without regard to case. Omit to match any sender.")] string? senderAddress = null,
        [Description("Count only mail addressed to this whole mail address in its To or Cc header. Omit to match any recipient.")] string? recipientAddress = null,
        [Description("Count only mail whose subject contains this text, without regard to case. Omit to match any subject.")] string? subjectFragment = null,
        [Description("Count only mail received at or after this moment, written as yyyy-MM-ddTHH:mm against the date and time the turn states, with no zone and no Z. Omit for no lower bound.")] string? receivedOnOrAfter = null,
        [Description("Count only mail received strictly before this moment, written as yyyy-MM-ddTHH:mm against the date and time the turn states, with no zone and no Z. Omit for no upper bound.")] string? receivedBefore = null,
        [Description("Count only mail this mailbox has opened (true) or not opened (false). Mail whose state nobody has observed yet counts as not opened. Omit to match either.")] bool? isMarkedSeen = null,
        [Description("Count only mail flagged (true) or unflagged (false). Omit to match either.")] bool? isFlagged = null,
        [Description("Count only mail carrying this keyword, a flag a mail client or server set such as $Junk or a label. Omit to match any keyword.")] string? keyword = null,
        [Description("Count only mail that carries attachments (true) or carries none (false). Omit to match either.")] bool? hasAttachments = null,
        [Description("Count only mail sorted into this category, under the same rule a search's category follows. Omit to match any category.")] string? category = null,
        [Description("Count only mail in the folder playing this role in each account: Inbox, Sent, Archive, Drafts, Trash, Flagged, or Important. Omit to count every folder.")] string? folder = null,
        CancellationToken cancellationToken = default)
    {
        await this.ReportAsync(AgentActivity.CountingMail, cancellationToken);

        if (DimensionOf(groupBy) is not { } dimension)
        {
            return "Give groupBy as sender, domain, day, week, month, or category, or leave it out for one total.";
        }

        if (this.ListingRequestOf(direction: null, receivedOnOrAfter, receivedBefore, folder, out var listing) is { } refusal)
        {
            return refusal;
        }

        var request = new CountEmailsRequest
        {
            Reader = listing.Reader,
            Folders = listing.Folders,
            SenderAddress = senderAddress,
            RecipientAddress = recipientAddress,
            SubjectFragment = subjectFragment,
            ReceivedOnOrAfter = listing.ReceivedOnOrAfter,
            ReceivedBefore = listing.ReceivedBefore,
            IsMarkedSeen = isMarkedSeen,
            IsFlagged = isFlagged,
            Keyword = keyword,
            HasAttachments = hasAttachments,
            Category = category,
            Dimension = dimension,
            MaximumGroups = groups,
            Zone = this.readers.Clock.Zone(),
        };

        MailCensus? census;

        try
        {
            census = await this.readers.Census.CountAsync(request, cancellationToken);
        }
        catch (MailFathomException refused) when (refused is MailboxQueryFilterInvalidException or MailboxQueryPageSizeOutOfRangeException)
        {
            // Told rather than raised, for the reason a listing's refusals are: the model can write a usable value.
            return refused.Message;
        }

        if (census is null)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"The count did not finish within {MailboxCensusReader.MaximumDuration.TotalSeconds:0} seconds. Narrow it — a shorter received period, one sender, one folder — and count again.");
        }

        var admitted = this.retrieval.RunLedger.AdmitCensusGroups(census.Groups);

        return AgentConversationReadings.Census(
            dimension,
            census with { Groups = await this.GuardedGroupsAsync(admitted, dimension, cancellationToken) },
            ceilingReached: admitted.Count < census.Groups.Count);
    }

    /// <summary>Reads what the model named to group by, or nothing where it named something no census groups by.</summary>
    private static MailCensusDimension? DimensionOf(string? groupBy) => groupBy?.Trim().ToUpperInvariant() switch
    {
        null or "" or "NONE" or "TOTAL" => MailCensusDimension.Total,
        "SENDER" => MailCensusDimension.SenderAddress,
        "DOMAIN" => MailCensusDimension.SenderDomain,
        "DAY" => MailCensusDimension.Day,
        "WEEK" => MailCensusDimension.Week,
        "MONTH" => MailCensusDimension.Month,
        "CATEGORY" => MailCensusDimension.Category,
        _ => null,
    };

    /// <summary>Guards every key somebody else wrote — a sender's address or its domain — before the reading is written around it.</summary>
    /// <remarks>
    /// A period is this deployment's own date and a category is a name the account declared, so neither is guarded. An
    /// address is counted in the case-folded form it is compared in, which is written in capitals; it is shown in lower
    /// case, the form a person reads and writes an address in, which loses nothing the folded form still held.
    /// </remarks>
    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "Shown to a model to write back to a person, never compared; the comparison form stays the upper-case one.")]
    private async Task<IReadOnlyList<MailCensusGroup>> GuardedGroupsAsync(
        IReadOnlyList<MailCensusGroup> groups,
        MailCensusDimension dimension,
        CancellationToken cancellationToken)
    {
        if (dimension is not (MailCensusDimension.SenderAddress or MailCensusDimension.SenderDomain))
        {
            return groups;
        }

        List<MailCensusGroup> guarded = [];

        foreach (var group in groups)
        {
            guarded.Add(group with
            {
                Key = await this.egressGuard.GuardOptionalAsync(SensitiveContentEgressPoint.ChatPrompt, group.Key?.ToLowerInvariant(), cancellationToken),
            });
        }

        return guarded;
    }

    /// <summary>Reads the arguments a listing takes in a form of the model's own, or says which one to write again.</summary>
    /// <remarks>
    /// The scope is not among them. The listing names no account, so it reads every account the run's scope was
    /// resolved over, under the reader the run reads as, which is what keeps the model from reaching past the mail its
    /// search and its conversation readings reach.
    /// </remarks>
    private string? ListingRequestOf(
        string? direction,
        string? receivedOnOrAfter,
        string? receivedBefore,
        string? folder,
        out ListEmailsRequest request)
    {
        request = new ListEmailsRequest { Reader = MailboxReaderKind.ConversationAgent };

        EmailTimelineDirection? order = direction?.Trim() switch
        {
            null or "" => EmailTimelineDirection.NewestFirst,
            var named when named.Equals("newestFirst", StringComparison.OrdinalIgnoreCase) => EmailTimelineDirection.NewestFirst,
            var named when named.Equals("oldestFirst", StringComparison.OrdinalIgnoreCase) => EmailTimelineDirection.OldestFirst,
            _ => null,
        };

        if (order is not { } timelineOrder)
        {
            return "Give the direction as newestFirst or oldestFirst, or leave it out.";
        }

        var opens = AnchoredInstant.Read(receivedOnOrAfter, this.retrieval.AskedAt);
        var closes = AnchoredInstant.Read(receivedBefore, this.retrieval.AskedAt);

        if ((!string.IsNullOrWhiteSpace(receivedOnOrAfter) && opens is null)
            || (!string.IsNullOrWhiteSpace(receivedBefore) && closes is null))
        {
            return $"Write each received bound as {AnchoredInstant.WrittenForm} against the date and time the turn states, with no zone and no Z.";
        }

        IReadOnlyList<MailFolderReference> folders = [];

        if (!string.IsNullOrWhiteSpace(folder))
        {
            if (!MailFolderReference.TryCreateNamedByCaller(MailFolderReference.RoleScheme + folder.Trim(), out var role))
            {
                return "Give the folder as one of the roles Inbox, Sent, Archive, Drafts, Trash, Flagged, or Important, or leave it out.";
            }

            folders = [role];
        }

        request = request with
        {
            Direction = timelineOrder,
            ReceivedOnOrAfter = opens,
            ReceivedBefore = closes,
            Folders = folders,
        };

        return null;
    }

    /// <summary>Reads where each listed message came from and guards every value its author wrote, before the page is written around them.</summary>
    /// <remarks>
    /// A message whose provenance is gone was removed after the page was read, and is not shown rather than shown
    /// without saying where it came from — which is also what marks a message the mailbox has not sent as unsent.
    /// </remarks>
    private async Task<IReadOnlyList<GuardedListedReading>> GuardedListingOfAsync(
        IReadOnlyList<EmailSummary> page,
        CancellationToken cancellationToken)
    {
        if (page.Count is 0)
        {
            return [];
        }

        var provenance = await this.readers.ProvenanceReader.ReadAsync(
            [.. page.Select(static summary => summary.StoredEmailId)],
            cancellationToken);

        List<GuardedListedReading> readings = [];

        foreach (var summary in page.Where(summary => provenance.ContainsKey(summary.StoredEmailId)))
        {
            readings.Add(new GuardedListedReading(
                summary.StoredEmailId,
                await this.egressGuard.GuardOptionalAsync(SensitiveContentEgressPoint.ChatPrompt, summary.SenderDisplayName, cancellationToken),
                await this.egressGuard.GuardOptionalAsync(SensitiveContentEgressPoint.ChatPrompt, summary.SenderAddress, cancellationToken),
                summary.ReceivedAt,
                summary.Flags.IsSeen,
                summary.Flags.IsFlagged,
                summary.Attachments.HasAttachments,
                provenance[summary.StoredEmailId],
                await this.egressGuard.GuardOptionalAsync(SensitiveContentEgressPoint.ChatPrompt, summary.Subject, cancellationToken)));
        }

        return readings;
    }

    [Description("Reads one email conversation.")]
    private async Task<string> ReadThreadAsync(
        [Description("The id of any message in the conversation, or of the conversation itself.")] string messageId,
        CancellationToken cancellationToken)
    {
        await this.ReportAsync(AgentActivity.ReadingThread, cancellationToken);

        if (await this.ReadConversationAsync(messageId, cancellationToken) is not { Messages.Count: > 0 } conversation)
        {
            return "No message with that id is readable here.";
        }

        var readings = await this.GuardedReadingsOfAsync(conversation.Messages, cancellationToken);

        return AgentConversationReadings.Thread(
            readings,
            conversation.ThreadId,
            conversation.EarlierLeftOut,
            conversation.LaterLeftOut);
    }

    /// <summary>Reads where each shown message came from and guards the ones it answers for.</summary>
    /// <remarks>
    /// A message whose provenance is gone was removed after the conversation was read, and is not shown at all rather
    /// than shown without saying where it came from.
    /// </remarks>
    internal async Task<IReadOnlyList<GuardedThreadReading>> GuardedReadingsOfAsync(
        IReadOnlyList<ReadEmailContent> shown,
        CancellationToken cancellationToken)
    {
        StoredEmailId[] messages = [.. shown.Select(static message => message.StoredEmailId)];
        var provenance = await this.readers.ProvenanceReader.ReadAsync(messages, cancellationToken);
        var attachmentReadings = await this.readers.AttachmentTexts.ReadReadingsAsync(messages, cancellationToken);

        List<GuardedThreadReading> readings = [];

        foreach (var message in shown.Where(message => provenance.ContainsKey(message.StoredEmailId)))
        {
            readings.Add(await this.GuardedReadingOfAsync(
                message,
                provenance[message.StoredEmailId],
                attachmentReadings.GetValueOrDefault(message.StoredEmailId, []),
                cancellationToken));
        }

        return readings;
    }

    /// <summary>Guards each value one message's author wrote, before the reading is written around it.</summary>
    /// <remarks>
    /// <para>
    /// The body is marked as cut where either this tool's bound or the guard's analyzed ceiling shortened it, because
    /// the ceiling drops what lies beyond it without failing and a reading that hid the drop would show the model a
    /// shortened message as whole. How long the whole body is travels beside it, so a model can tell a short message
    /// from the opening of a long one and read on rather than answer from the opening.
    /// </para>
    /// <para>
    /// Every file the message carries is named whether or not anything read it, with the name its sender chose guarded
    /// like any other text the sender wrote.
    /// </para>
    /// </remarks>
    internal async Task<GuardedThreadReading> GuardedReadingOfAsync(
        ReadEmailContent message,
        EmailProvenance provenance,
        IReadOnlyList<AttachmentTextReading> attachmentReadings,
        CancellationToken cancellationToken)
    {
        var body = message.Body.PlainText.Text;
        var bounded = MailTextBounds.TruncateAtTextElementBoundary(body, MaximumBodyCharacters);

        this.readBodies.Add(bounded);

        var sender = await this.egressGuard.GuardOptionalAsync(SensitiveContentEgressPoint.ChatPrompt, SenderOf(message)?.Address, cancellationToken);
        var subject = await this.egressGuard.GuardOptionalAsync(SensitiveContentEgressPoint.ChatPrompt, message.Headers.Subject, cancellationToken);
        var guardedBody = await this.egressGuard.GuardWithOmissionAsync(SensitiveContentEgressPoint.ChatPrompt, bounded, cancellationToken);
        var citation = await this.CiteAsync(message.StoredEmailId, message.Headers.Subject, cancellationToken);
        var footed = await this.FootedAccountsAsync(cancellationToken);
        var attachments = await this.GuardedAttachmentsOfAsync(message, attachmentReadings, cancellationToken);

        return new GuardedThreadReading(
            message.StoredEmailId,
            citation.Id,
            sender,
            message.Headers.SentAt,
            provenance,
            subject,
            guardedBody.Text,
            bounded.Length < body.Length || guardedBody.WasCutAtAnalyzedCeiling,
            await AgentReceiptReadings.GuardedAsync(this.egressGuard, message.Receipts, cancellationToken),
            footed.Contains(message.AccountId),
            body.Length,
            bounded.Length,
            attachments);
    }

    /// <summary>Names every file one message carries, beside what reading each came to, its sender's name for it guarded.</summary>
    private async Task<IReadOnlyList<GuardedAttachmentReading>> GuardedAttachmentsOfAsync(
        ReadEmailContent message,
        IReadOnlyList<AttachmentTextReading> attachmentReadings,
        CancellationToken cancellationToken)
    {
        var byPosition = attachmentReadings.ToDictionary(static reading => reading.AttachmentPosition);
        List<GuardedAttachmentReading> attachments = [];

        foreach (var (attachment, position) in message.Attachments.Select(static (attachment, position) => (attachment, position)))
        {
            this.shownFiles.Add((message.StoredEmailId, position));
            attachments.Add(await this.GuardedAttachmentOfAsync(attachment, position, byPosition.GetValueOrDefault(position), cancellationToken));
        }

        return attachments;
    }

    private async Task<GuardedAttachmentReading> GuardedAttachmentOfAsync(
        ReadEmailAttachment attachment,
        int position,
        AttachmentTextReading? reading,
        CancellationToken cancellationToken) => new(
        position,
        await this.egressGuard.GuardOptionalAsync(SensitiveContentEgressPoint.ChatPrompt, attachment.Description.FileName?.Value, cancellationToken),
        attachment.Description.MediaType,
        attachment.Description.DecodedSizeOctets,
        reading);

    [Description("Reads on in one message's body.")]
    private async Task<string> ReadMessageAsync(
        [Description("The id of the message, as a conversation reading or a search showed it.")] string messageId,
        [Description("Where to start, in characters from the beginning of the body: the next attribute of the page before. Omit to start at the beginning.")] int? from = null,
        CancellationToken cancellationToken = default)
    {
        await this.ReportAsync(AgentActivity.ReadingThread, cancellationToken);

        if (await this.ReadOneMessageAsync(messageId, cancellationToken) is not { } message)
        {
            return "No message with that id is readable here.";
        }

        var body = message.Body.PlainText.Text;
        var start = StartWithin(body, from);

        if (start >= body.Length)
        {
            return string.Create(CultureInfo.InvariantCulture, $"The body holds {body.Length} characters, so nothing remains to read from {from ?? 0}.");
        }

        var (page, ceilingReached) = this.AdmittedPage(message.StoredEmailId, body, start);

        if (page.Length is 0)
        {
            return CeilingReachedStatement;
        }

        // The page was spent from the ceiling before the scan so the scanner never sees text the run may not send; a
        // tail the analyzed ceiling dropped is therefore charged without leaving, which errs towards sending less.
        var guarded = await this.egressGuard.GuardWithOmissionAsync(SensitiveContentEgressPoint.ChatPrompt, page, cancellationToken);
        var shown = page.Length - guarded.OmittedCharacterCount;

        this.readBodies.Add(page[..shown]);

        var citation = await this.CiteAsync(message.StoredEmailId, message.Headers.Subject, cancellationToken);

        return AgentConversationReadings.MessagePage(new GuardedMessagePage(
            message.StoredEmailId,
            citation.Id,
            new PageBounds(body.Length, start, start + shown, ceilingReached),
            guarded.Text));
    }

    [Description("Reads one file a message carries.")]
    private async Task<string> ReadAttachmentAsync(
        [Description("The id of the message carrying the file, as a conversation reading or a search showed it.")] string messageId,
        [Description("The file's position, the attachment attribute a conversation reading or a search showed for it.")] int attachment,
        [Description("The page, slide, or sheet number to start at, counted from 1. Omit to start at the beginning or to continue with from.")] int? page = null,
        [Description("Where to continue, in characters from the beginning of the file's words: the next attribute of the page before. Omit to start at the beginning or at the page you named.")] int? from = null,
        CancellationToken cancellationToken = default)
    {
        await this.ReportAsync(AgentActivity.ReadingThread, cancellationToken);

        if (await this.ReadOneMessageAsync(messageId, cancellationToken) is not { } message)
        {
            return "No message with that id is readable here.";
        }

        if (attachment < 0 || attachment >= message.Attachments.Count)
        {
            return string.Create(CultureInfo.InvariantCulture, $"That message carries {message.Attachments.Count} files, at positions counted from 0; a conversation reading names each.");
        }

        this.shownFiles.Add((message.StoredEmailId, attachment));

        var content = await this.readers.AttachmentTexts.ReadTextAsync(message.StoredEmailId, attachment, cancellationToken);
        var citation = await this.CiteAsync(message.StoredEmailId, message.Headers.Subject, cancellationToken);
        var described = await this.GuardedAttachmentOfAsync(message.Attachments[attachment], attachment, content?.Reading, cancellationToken);

        if (content is not { Reading.WasRead: true } read)
        {
            return AgentConversationReadings.AttachmentUnread(message.StoredEmailId, citation.Id, described);
        }

        if (StartOfPlace(read, page, from) is not { } start)
        {
            return string.Create(CultureInfo.InvariantCulture, $"The file was read into {read.Reading.PageCount} places numbered from 1; name one of them, or leave the page out.");
        }

        if (start >= read.Text.Length)
        {
            return string.Create(CultureInfo.InvariantCulture, $"The file's words hold {read.Text.Length} characters, so nothing remains to read from {start}.");
        }

        var (window, ceilingReached) = this.AdmittedPage(message.StoredEmailId, read.Text, start);

        if (window.Length is 0)
        {
            return CeilingReachedStatement;
        }

        var (parts, end) = await GuardedPartsAsync(this.egressGuard, read.Text, read.Segments, start, window.Length, cancellationToken);

        this.readBodies.Add(read.Text[start..end]);

        return AgentConversationReadings.AttachmentPage(new GuardedAttachmentPage(
            message.StoredEmailId,
            citation.Id,
            attachment,
            described.FileName,
            read.Reading.Kind,
            read.Reading.PageCount,
            read.Reading.PageKind,
            new PageBounds(read.Text.Length, start, end, ceilingReached),
            parts));
    }

    /// <summary>Reads one message the person may read, with nothing of its conversation, or nothing where it is not one.</summary>
    /// <remarks>
    /// The content read is what admits the message: a message outside the person's scope reads as no message at all,
    /// which is also what keeps a file's words from being reached by naming an identifier. A message whose provenance
    /// is gone was removed after it was read and is not shown, for the reason a conversation reading leaves one out.
    /// </remarks>
    private async Task<ReadEmailContent?> ReadOneMessageAsync(string messageId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(messageId, out var parsed))
        {
            return null;
        }

        var named = StoredEmailId.Create(parsed);
        var opened = await this.readers.ContentReader.ReadContentAsync(
            GetEmailContentRequest.Create([named]) with { Reader = MailboxReaderKind.ConversationAgent },
            cancellationToken);

        if (opened.Emails.Count is 0 || opened.Emails[0].Content is not { } message)
        {
            return null;
        }

        var provenance = await this.readers.ProvenanceReader.ReadAsync([named], cancellationToken);

        return provenance.ContainsKey(named) ? message : null;
    }

    /// <summary>Cuts one page out of a whole, as much of it as the run may still send.</summary>
    /// <returns>The page, empty once the run's ceiling is spent, and whether the ceiling cut it short.</returns>
    private (string Page, bool CeilingReached) AdmittedPage(StoredEmailId message, string whole, int start)
    {
        var page = MailTextBounds.TruncateAtTextElementBoundary(whole[start..], MaximumPageCharacters);
        var granted = this.retrieval.RunLedger.AdmitPage(message, page.Length);

        return granted < page.Length
            ? (MailTextBounds.TruncateAtTextElementBoundary(page, granted), true)
            : (page, false);
    }

    /// <summary>Reads where a page begins: at the place named, at a character the model was handed, or at the beginning.</summary>
    /// <returns>The offset, or <see langword="null" /> where the place named is not one the file was read into.</returns>
    private static int? StartOfPlace(AttachmentTextContent read, int? place, int? from)
    {
        if (from is not null || place is null)
        {
            return StartWithin(read.Text, from);
        }

        return read.Segments.FirstOrDefault(segment => segment.Number == place)?.StartOffset;
    }

    /// <summary>Reads a start the model wrote, held inside the text and never between the halves of a surrogate pair.</summary>
    internal static int StartWithin(string text, int? from)
    {
        var start = Math.Clamp(from ?? 0, 0, text.Length);

        return start > 0 && start < text.Length && char.IsLowSurrogate(text[start]) ? start - 1 : start;
    }

    /// <summary>Cuts a page of a file's words at the places it crosses and guards each part on its own.</summary>
    /// <returns>The guarded parts, and where in the file's words the last character they show stands.</returns>
    /// <remarks>
    /// <para>
    /// Each part is guarded as a value rather than the page as a whole, for the reason every other reading guards its
    /// values one by one: a detection running across a page boundary would otherwise take the element between them.
    /// </para>
    /// <para>
    /// A part the guard's analyzed ceiling shortened ends the page there, so the page never claims to have shown what
    /// the guard dropped and the next page begins at the first character the model has not seen.
    /// </para>
    /// </remarks>
    internal static async Task<(IReadOnlyList<GuardedAttachmentPart> Parts, int End)> GuardedPartsAsync(
        SensitiveContentEgressGuard egressGuard,
        string text,
        IReadOnlyList<AttachmentTextSegment> segments,
        int start,
        int length,
        CancellationToken cancellationToken)
    {
        var end = start + length;
        int[] cuts =
        [
            start,
            .. segments.Select(static segment => segment.StartOffset).Where(offset => offset > start && offset < end),
            end,
        ];
        List<GuardedAttachmentPart> parts = [];

        foreach (var (opens, closes) in cuts.Zip(cuts.Skip(1)))
        {
            var guarded = await egressGuard.GuardWithOmissionAsync(
                SensitiveContentEgressPoint.ChatPrompt,
                text[opens..closes],
                cancellationToken);

            parts.Add(new GuardedAttachmentPart(AttachmentTextSegment.At(segments, opens), guarded.Text));

            if (guarded.WasCutAtAnalyzedCeiling)
            {
                return (parts, closes - guarded.OmittedCharacterCount);
            }
        }

        return (parts, end);
    }

    /// <summary>Reads, once per run, which of the person's accounts append their footer to what they send.</summary>
    /// <remarks>
    /// Read only where the run may propose mail, because it is a proposal's wording the answer shapes, and never the
    /// footer itself: what the model is told is that the closing is taken care of, not who it names.
    /// </remarks>
    private async Task<IReadOnlySet<MailAccountId>> FootedAccountsAsync(CancellationToken cancellationToken)
    {
        if (this.footedAccounts is null)
        {
            HashSet<MailAccountId> footed = [];

            if (this.ProposesMail)
            {
                var user = this.readers.Authorization.RequireUser();

                foreach (var account in this.accounts)
                {
                    if (await this.readers.Footers.HasFooterAsync(user, account, cancellationToken))
                    {
                        footed.Add(account);
                    }
                }
            }

            this.footedAccounts = footed;
        }

        return this.footedAccounts;
    }

    [Description("Shows the person the state of one conversation.")]
    private async Task<string> ShowThreadStateAsync(
        [Description("The id of any message in the conversation, or of the conversation itself.")] string messageId,
        CancellationToken cancellationToken)
    {
        await this.ReportAsync(AgentActivity.ReadingThread, cancellationToken);

        if (await this.ReadConversationAsync(messageId, cancellationToken) is not { ThreadId: { } thread } conversation
            || await this.readers.StateBrowser.ReadStateAsync(thread, MailboxReaderKind.Agent, cancellationToken) is not { } state
            || await this.ThreadStateBlockOfAsync(state, conversation.Messages, cancellationToken) is not { } block)
        {
            return "This conversation has no reading yet. Answer from its messages instead.";
        }

        await this.WriteAsync(this.journal.ComposeAsync(block, cancellationToken));

        return "The person now sees this conversation's state as it was derived. Do not restate or translate it; refer to it.";
    }

    [Description("Hands the person one file a message carries.")]
    private async Task<string> OfferAttachmentAsync(
        [Description("The id of the message carrying the file, as a conversation reading or a search showed it.")] string messageId,
        [Description("The file's position, the attachment attribute a conversation reading or a search showed for it.")] int attachment,
        CancellationToken cancellationToken)
    {
        await this.ReportAsync(AgentActivity.ComposingAnswer, cancellationToken);

        if (!Guid.TryParse(messageId, out var named) || !this.WasShown(StoredEmailId.Create(named), attachment))
        {
            return $"That file is not one this conversation has shown you. Read the message's conversation with {ReadThreadToolName}, which names every file it carries, and offer one of those.";
        }

        if (await this.ReadOneMessageAsync(messageId, cancellationToken) is not { } message || attachment >= message.Attachments.Count)
        {
            return "No message with that id is readable here.";
        }

        if (!this.offeredFiles.Add((message.StoredEmailId, attachment)))
        {
            return "That file is already in your answer.";
        }

        var citation = await this.CiteAsync(message.StoredEmailId, message.Headers.Subject, cancellationToken);
        var block = FileBlockOf(message, attachment, citation.Id);

        await this.WriteAsync(this.journal.ComposeAsync(block, cancellationToken));

        return "Offered. The person downloads the whole file from the card in your answer; refer to it rather than repeating its name, type, or size. Offering it says nothing about what it holds.";
    }

    /// <summary>Reads whether a conversation reading, a file reading, or a search showed the model this file.</summary>
    /// <remarks>
    /// Only a file the run was shown may be offered, so a model cannot hand the person a file by guessing a position on
    /// a message it was only told carries attachments; the read that follows then holds the offer to the person's grant.
    /// </remarks>
    private bool WasShown(StoredEmailId message, int position) =>
        this.shownFiles.Contains((message, position))
        || this.retrieval.Report.Passages.Any(passage =>
            passage.StoredEmailId == message
            && passage.AttachmentExtracts.Any(extract => extract.AttachmentPosition == position));

    /// <summary>Describes one file as the message says it, beside the citation of the message it arrived on.</summary>
    /// <remarks>
    /// Written for the person rather than for the model, so the sender's name for the file is kept as it was given
    /// rather than guarded; a media type a presentation text refuses reads as the general binary type, which is what a
    /// download of it is served as.
    /// </remarks>
    private static FileBlock FileBlockOf(ReadEmailContent message, int position, PresentationCitationId citation)
    {
        var description = message.Attachments[position].Description;
        var sender = SenderOf(message);

        return new FileBlock(
            EvidenceOf((message.StoredEmailId, citation)),
            citation,
            position,
            PresentationText.TryCreate(description.FileName?.Value, out var name) ? name : null,
            PresentationText.TryCreate(description.MediaType, out var mediaType) ? mediaType : PresentationText.Create(GeneralBinaryMediaType),
            description.DecodedSizeOctets,
            PresentationText.TryCreate(sender?.DisplayName ?? sender?.Address, out var from) ? from : null,
            message.Headers.SentAt);
    }

    [Description("Writes a report and hands it to the person as a PDF.")]
    private async Task<string> WriteReportAsync(
        [Description("The report's title, in the person's language.")] string title,
        [Description("The report itself, in Markdown, citing each statement's source with [^id].")] string markdown,
        CancellationToken cancellationToken)
    {
        await this.ReportAsync(AgentActivity.ComposingAnswer, cancellationToken);

        if (this.reporting is not { } reporting)
        {
            return "No report can be written in this conversation.";
        }

        if (this.reportWritten)
        {
            return "This answer already carries a report; a run writes one. Refer to it in your answer.";
        }

        var titled = TitleOf(title);

        if (titled.Length is 0 || string.IsNullOrWhiteSpace(markdown))
        {
            return "A report has a title and a body.";
        }

        if (titled.Length > AgentReportBounds.MaximumTitleCharacters || markdown.Length > AgentReportBounds.MaximumBodyCharacters)
        {
            return string.Create(CultureInfo.InvariantCulture, $"A report's title is at most {AgentReportBounds.MaximumTitleCharacters} characters and its body at most {AgentReportBounds.MaximumBodyCharacters}; write it shorter.");
        }

        var (body, sources, citations) = await this.CitedAsync(markdown, reporting, cancellationToken);
        var writtenAt = this.readers.Clock.Now();
        var document = this.readers.Reports.Typeset(new AgentReport(titled, body, writtenAt, reporting.Language, sources));
        var intake = await this.readers.Files.TakeAsync(reporting.Question.Conversation, reporting.Question.User, FileNameOf(titled), document.Content, cancellationToken);

        if (intake.File is not { } stored)
        {
            return intake.Refusal is AgentConversationFileRefusal.ConversationFull
                ? string.Create(CultureInfo.InvariantCulture, $"The report was not written: this conversation already holds the {AgentConversationFileBounds.MaximumFilesPerConversation} files one may. Tell the person, and give them your findings in the answer instead.")
                : "The report could not be stored. Tell the person, and give them your findings in the answer instead.";
        }

        this.reportWritten = true;

        await this.WriteAsync(this.journal.ComposeAsync(ReportBlockOf(stored, titled, document, writtenAt, citations), cancellationToken));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Written: a {document.PageCount}-page PDF in your answer, closing with the {sources.Count} sources it cites. Do not repeat the report in your answer: say in a sentence or two that you wrote it, what it covers, and what it leaves out.");
    }

    /// <summary>Resolves every citation the model wrote into the report's own numbering, keeping only what the run was shown.</summary>
    /// <returns>The body with each citation numbered, the sources in that numbering, and the citations of the messages among them.</returns>
    /// <remarks>
    /// Messages are numbered first, in the order the body first cites them, and the question's files after them in the
    /// same order. A message is a source only where this run was shown it and the person may still read it, and a file
    /// only where the question carries it; any other citation is taken out of the body rather than printed, so nothing
    /// the model names becomes a source a reader would look for.
    /// </remarks>
    private async Task<(string Body, IReadOnlyList<AgentReportSource> Sources, IReadOnlyList<PresentationCitationId> Citations)> CitedAsync(
        string markdown,
        AgentConversationReporting reporting,
        CancellationToken cancellationToken)
    {
        var zone = this.readers.Clock.Zone();
        var named = ReportCitation().Matches(markdown).Select(static match => match.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var carried = reporting.Files.Held.Where(file => reporting.Question.Files.Contains(file.Id)).ToDictionary(static file => file.Id.Value);
        List<(string Named, AgentReportSource Source, PresentationCitationId Citation)> messages = [];
        List<(string Named, AgentReportSource Source)> files = [];

        foreach (var name in named.Where(static name => Guid.TryParse(name, out _)))
        {
            if (messages.Count + files.Count >= AgentReportBounds.MaximumSources)
            {
                break;
            }

            if (carried.TryGetValue(Guid.Parse(name), out var file))
            {
                files.Add((name, new AgentReportSource(AgentReportSourceKind.AttachedFile, file.Name ?? file.Id.ToString(), Author: null, zone.Reading(file.AddedAt))));
            }
            else if (await this.SourceOfAsync(name, cancellationToken) is { } source
                && await this.ReadOneMessageAsync(name, cancellationToken) is { } message)
            {
                var sender = SenderOf(message);

                messages.Add((
                    name,
                    new AgentReportSource(
                        AgentReportSourceKind.Message,
                        string.IsNullOrWhiteSpace(message.Headers.Subject) ? this.cited[source.Message].Label.Value : message.Headers.Subject,
                        sender?.DisplayName ?? sender?.Address,
                        message.Headers.SentAt is { } sentAt ? zone.Reading(sentAt) : null),
                    source.Citation));
            }
        }

        var numbers = messages.Select(static entry => entry.Named).Concat(files.Select(static entry => entry.Named))
            .Select(static (name, index) => (name, index))
            .ToDictionary(static entry => entry.name, static entry => entry.index + 1, StringComparer.OrdinalIgnoreCase);
        var body = ReportCitation().Replace(markdown, match =>
            numbers.TryGetValue(match.Groups[1].Value, out var number) ? AgentReport.CitationOf(number) : string.Empty);

        return (
            body,
            [.. messages.Select(static entry => entry.Source), .. files.Select(static entry => entry.Source)],
            [.. messages.Select(static entry => entry.Citation)]);
    }

    /// <summary>Reads a title the model wrote as one line of text.</summary>
    private static string TitleOf(string? title) =>
        new([.. string.Join(' ', (title ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Where(static character => !char.IsControl(character))]);

    /// <summary>Names the file a report is saved under: its title, with what a file name may not carry replaced.</summary>
    private static string FileNameOf(string title) =>
        string.Concat(title.Select(static character => FileNameReservedCharacters.Contains(character) ? '-' : character)) + ".pdf";

    private static ReportBlock ReportBlockOf(
        AgentConversationFile stored,
        string title,
        AgentReportDocument document,
        DateTimeOffset writtenAt,
        IReadOnlyList<PresentationCitationId> citations) =>
        new(
            citations.Count is 0
                ? PresentationEvidence.Unsupported(PresentationFreshness.Unknown)
                : new PresentationEvidence(PresentationSupport.Cited, [.. citations.Take(PresentationEvidence.MaxCitations)], PresentationFreshness.Unknown),
            stored.Id,
            PresentationText.TryCreate(title, out var titled) ? titled : PresentationText.Create(FileNameOf(title)),
            PresentationText.TryCreate(stored.Name, out var name) ? name : PresentationText.Create(FileNameOf(title)),
            PresentationText.Create(stored.MediaType),
            stored.ByteLength,
            document.PageCount,
            writtenAt);

    [GeneratedRegex(@"\[\^([^\]\s]{1,64})\]", RegexOptions.CultureInvariant)]
    private static partial Regex ReportCitation();

    [Description("Reads the person's calendar.")]
    private async Task<string> ReadCalendarAsync(
        [Description("Where the window opens, as an ISO 8601 instant with an offset.")] string from,
        [Description("Where the window closes, as an ISO 8601 instant with an offset.")] string until,
        CancellationToken cancellationToken)
    {
        await this.ReportAsync(AgentActivity.ReadingCalendar, cancellationToken);

        if (!TryParseInstant(from, out var opens)
            || !TryParseInstant(until, out var closes)
            || closes <= opens
            || closes - opens > MaximumCalendarWindow)
        {
            return $"Give two ISO 8601 instants with an offset, the second later than the first and at most {MaximumCalendarWindow.TotalDays} days after it.";
        }

        var events = await this.readers.Calendar.ReadWindowAsync(opens, closes, origin: null, MaximumAgendaItems, cancellationToken) ?? [];

        if (events.Count is 0)
        {
            return "Nothing is on the calendar in that window.";
        }

        var titles = await this.egressGuard.GuardAllAsync(
            SensitiveContentEgressPoint.ChatPrompt,
            [.. events.Select(static entry => entry.Title.Value)],
            cancellationToken);

        return AgentConversationReadings.Calendar(
            opens,
            closes,
            [.. events.Select((entry, index) => new GuardedCalendarReading(entry.Start, entry.End, entry.IsAllDay, titles[index]))]);
    }

    /// <summary>Reads an instant only where it states its offset, so none is silently read against this server's own time zone.</summary>
    private static bool TryParseInstant(string text, out DateTimeOffset instant)
    {
        instant = default;

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var stated)
            && stated.Kind is not DateTimeKind.Unspecified
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out instant);
    }

    /// <summary>Reads the time a proposed message is to leave at, refusing one a send could not be held until.</summary>
    /// <param name="sendAt">What the model gave, or nothing for a message sent once accepted.</param>
    /// <param name="due">The time with the person's zone, or <see langword="null" /> where none was given.</param>
    /// <returns>A refusal the model can correct, or <see langword="null" /> where the time stands.</returns>
    /// <remarks>
    /// A time already gone is refused here rather than at the acceptance, because the send would refuse it there anyway,
    /// and a model told now can still ask for a later one. So is an offset the person's zone does not stand at on that
    /// day: the turn states the offset of the moment it was asked, and a time named across a daylight-saving change
    /// written with it would leave an hour away from the one the person named. The zone recorded is the person's profile
    /// zone, which the turn named, so the outbox words the time the way they asked for it.
    /// </remarks>
    private string? RefuseSendTime(string? sendAt, out AgentSendTime? due)
    {
        due = null;

        if (string.IsNullOrWhiteSpace(sendAt))
        {
            return null;
        }

        if (!TryParseInstant(sendAt, out var leaves))
        {
            return "Give the send time as an ISO 8601 instant with an offset, or leave it empty to send once the person accepts.";
        }

        var now = this.readers.Clock.Now();

        if (leaves <= now)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"That time has already passed: it is {now:yyyy-MM-dd'T'HH:mmzzz} now. Give a later time, or leave it empty to send once the person accepts.");
        }

        var zone = this.readers.Clock.Zone();
        var there = zone.Reading(leaves);

        if (there.Offset != leaves.Offset)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"The offset {leaves:zzz} is not {zone.Id}'s at that time, which is UTC{there:zzz}. Give the time the person named with that offset.");
        }

        due = new AgentSendTime(leaves, zone.Id);

        return null;
    }

    private static string Proposed(AgentSendTime? due) => due is null
        ? "Proposed. Nothing was sent; the person reviews the draft and decides."
        : "Proposed to leave at the time you gave. Nothing was sent or scheduled; the person reviews the draft and decides.";

    [Description("Reads the person's tasks.")]
    private async Task<string> ReadTasksAsync(CancellationToken cancellationToken)
    {
        await this.ReportAsync(AgentActivity.ReadingTasks, cancellationToken);

        List<PersonalTask> read = [];

        foreach (var origin in Enum.GetValues<PersonalTaskOrigin>())
        {
            read.AddRange((await this.readers.Tasks.ReadPageAsync(origin, MaximumAgendaItems, cursor: null, cancellationToken))?.Tasks ?? []);
        }

        if (read.Count is 0)
        {
            return "The task list is empty.";
        }

        var titles = await this.egressGuard.GuardAllAsync(
            SensitiveContentEgressPoint.ChatPrompt,
            [.. read.Select(static task => task.Title)],
            cancellationToken);

        return AgentConversationReadings.Tasks(
            [.. read.Select((task, index) => new GuardedTaskReading(task.IsCompleted, task.Origin, task.DueOn, titles[index]))]);
    }

    [Description("Proposes a new email.")]
    private async Task<string> ProposeMessageAsync(
        [Description("The account it is sent from, one of the account names the turn lists.")] string account,
        [Description("Every recipient's email address.")] string[] recipients,
        [Description("The subject.")] string subject,
        [Description("The body, as plain text, in the language its recipients read. From an account the turn says closes with the person's footer, it ends at its own last sentence, with no closing phrase and no sign-off.")] string body,
        [Description(SendAtDescription)] string? sendAt,
        CancellationToken cancellationToken)
    {
        await this.ReportAsync(AgentActivity.PreparingProposal, cancellationToken);

        var sender = this.accounts.Where(known => string.Equals(known.Value, account, StringComparison.Ordinal)).ToArray();

        if (sender.Length is 0)
        {
            return $"Name one of these accounts: {string.Join(", ", this.accounts.Select(static known => known.Value))}.";
        }

        if (recipients is null
            || AddressesOf(recipients) is not { } addressed
            || !PresentationText.TryCreate(subject, out var titled)
            || !PresentationText.TryCreate(body, out var written))
        {
            return "Give at least one valid address, at most twenty, a subject, and a body.";
        }

        if (this.RefuseSendTime(sendAt, out var due) is { } refusal)
        {
            return refusal;
        }

        var block = new DraftBlock(
            PresentationEvidence.Unsupported(PresentationFreshness.Unknown),
            addressed,
            titled,
            written,
            DraftDisposition.Composed,
            due?.Instant,
            PresentationText.Create(sender[0].Value));

        await this.WriteAsync(this.journal.ProposeAsync(block, new AgentMessageSending(sender[0].Value, addressed, titled, written, due), cancellationToken));
        this.HasProposed = true;

        return Proposed(due);
    }

    [Description("Proposes an answer to a message.")]
    private async Task<string> ProposeReplyAsync(
        [Description("The id of the message being answered.")] string messageId,
        [Description("reply, replyToAll, or forward.")] string act,
        [Description("The body, as plain text, in the language the conversation is written in. Answering a message a conversation reading marks footer=\"true\", it ends at its own last sentence, with no closing phrase and no sign-off.")] string body,
        [Description("For a forward, every recipient's email address; for a reply, leave empty to answer whoever the message came from.")] string[]? recipients,
        [Description(SendAtDescription)] string? sendAt,
        CancellationToken cancellationToken)
    {
        await this.ReportAsync(AgentActivity.PreparingProposal, cancellationToken);

        if (!Guid.TryParse(messageId, out var answered)
            || !Enum.TryParse<AuthoredResponseAct>(act, ignoreCase: true, out var responseAct)
            || !Enum.IsDefined(responseAct)
            || !PresentationText.TryCreate(body, out var written))
        {
            return "Give a message id, one of reply, replyToAll, or forward, and a body.";
        }

        if (this.RefuseSendTime(sendAt, out var due) is { } refusal)
        {
            return refusal;
        }

        IReadOnlyList<EmailAddress> named = [];

        if (recipients is { Length: > 0 })
        {
            if (AddressesOf(recipients) is not { } parsed)
            {
                return "Give valid recipient addresses, at most twenty.";
            }

            named = parsed;
        }

        var answeredId = StoredEmailId.Create(answered);
        var authored = await this.readers.ResponseAuthoring.AuthorAsync(
            new AuthoredResponseRequest
            {
                AnsweredEmailId = answeredId,
                Reader = MailboxReaderKind.Agent,
                Act = responseAct,
                PlainTextBody = written.Value,
                Recipients = [.. named.Select(static address => NamedRecipient.AtAddress(OutgoingRecipientRole.To, address.Address, address.DisplayName))],
            },
            cancellationToken);

        if (authored.Email is not { } email
            || AddressesOf([.. email.Recipients.Select(static recipient => recipient.Address)]) is not { } addressed
            || !PresentationText.TryCreate(email.Subject, out var titled))
        {
            return "That message cannot be answered that way from here.";
        }

        var citation = await this.CiteAsync(answeredId, email.Subject, cancellationToken);

        // Cited rather than supported: the reply is the model's own prose, and nothing held it against the message it answers.
        var block = new DraftBlock(
            new PresentationEvidence(PresentationSupport.Cited, [citation.Id], PresentationFreshness.Unknown),
            addressed,
            titled,
            written,
            DraftDisposition.Composed,
            due?.Instant,
            PresentationText.Create(authored.Account.Value));

        await this.WriteAsync(this.journal.ProposeAsync(block, new AgentResponseSending(answeredId, responseAct, addressed, written, due), cancellationToken));
        this.HasProposed = true;

        return Proposed(due);
    }

    [Description("Proposes a calendar event.")]
    private async Task<string> ProposeEventAsync(
        [Description("What the event is called.")] string title,
        [Description("When it begins, as an ISO 8601 instant with an offset.")] string start,
        [Description("When it ends, as an ISO 8601 instant with an offset, or empty to state no end.")] string? end,
        [Description("True where it is a whole day rather than a clock time.")] bool allDay,
        [Description("The id of a message this conversation has already shown you that the event was read out of, or empty.")] string? messageId,
        CancellationToken cancellationToken)
    {
        await this.ReportAsync(AgentActivity.PreparingProposal, cancellationToken);

        if (!CalendarEventTitle.TryCreate(title, out _)
            || !PresentationText.TryCreate(title, out var titled)
            || !TryParseInstant(start, out var begins))
        {
            return $"Give a title of at most {CalendarEventTitle.MaximumLength} characters and a start as an ISO 8601 instant with an offset.";
        }

        DateTimeOffset? ends = null;

        if (!string.IsNullOrWhiteSpace(end))
        {
            if (!TryParseInstant(end, out var closes) || closes <= begins)
            {
                return "Give an end as an ISO 8601 instant with an offset, later than the start, or leave it out.";
            }

            ends = closes;
        }

        var source = await this.SourceOfAsync(messageId, cancellationToken);
        var block = new EventProposalBlock(EvidenceOf(source), titled, begins, ends, allDay);

        await this.WriteAsync(this.journal.ProposeAsync(
            block,
            new AgentEventScheduling(titled, begins, ends, allDay, source?.Message),
            cancellationToken));
        this.HasProposed = true;

        return $"Proposed. Nothing was put on the calendar; the person reviews the event and decides.{UncitedNote(messageId, source)}";
    }

    [Description("Proposes a task.")]
    private async Task<string> ProposeTaskAsync(
        [Description("The line the task list is drawn with.")] string title,
        [Description("The day it is due, as yyyy-MM-dd, or empty where nothing says when.")] string? dueOn,
        [Description("The id of a message this conversation has already shown you that the task comes from, or empty.")] string? messageId,
        CancellationToken cancellationToken)
    {
        await this.ReportAsync(AgentActivity.PreparingProposal, cancellationToken);

        if (!PresentationText.TryCreate(title, out var titled) || titled.Value.Length > PersonalTask.MaximumTitleLength)
        {
            return $"Give a title of at most {PersonalTask.MaximumTitleLength} characters.";
        }

        DateOnly? due = null;

        if (!string.IsNullOrWhiteSpace(dueOn))
        {
            if (!DateOnly.TryParseExact(dueOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            {
                return "Give the due day as yyyy-MM-dd, or leave it out.";
            }

            due = day;
        }

        var source = await this.SourceOfAsync(messageId, cancellationToken);
        var block = new TaskProposalBlock(EvidenceOf(source), titled, due);

        await this.WriteAsync(this.journal.ProposeAsync(
            block,
            new AgentTaskRecording(titled, due, source?.Message),
            cancellationToken));
        this.HasProposed = true;

        return $"Proposed. Nothing was added to the list; the person reviews the task and decides.{UncitedNote(messageId, source)}";
    }

    /// <summary>Reads the message a proposal cites out of what this run has already shown the model.</summary>
    /// <remarks>
    /// <para>
    /// A citation is minted from what a tool read, never from a name a model supplied, so a proposal rests on a message
    /// this run actually opened or searched up, or on nothing at all. That is also what keeps an invented identifier from
    /// becoming a source a reader is invited to follow.
    /// </para>
    /// <para>
    /// A message the search or a listing showed is declared here rather than left to
    /// <see cref="DeclareSearchedAndListedAsync" />, which runs once the run has ended: finding mail and then proposing
    /// out of what was found is the ordinary path, and waiting for the end would leave every such proposal uncited.
    /// </para>
    /// </remarks>
    private async Task<(StoredEmailId Message, PresentationCitationId Citation)?> SourceOfAsync(
        string? messageId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(messageId, out var named))
        {
            return null;
        }

        var message = StoredEmailId.Create(named);

        if (this.cited.TryGetValue(message, out var known))
        {
            return (message, known.Id);
        }

        string? subject;

        if (this.retrieval.Report.Passages.FirstOrDefault(passage => passage.StoredEmailId == message) is { } searched)
        {
            subject = searched.Subject;
        }
        else if (!this.listed.TryGetValue(message, out subject))
        {
            return null;
        }

        var citation = await this.CiteAsync(message, subject, cancellationToken);

        return (message, citation.Id);
    }

    // Cited rather than supported: the model named the message it read the proposal out of, and nothing held the
    // proposal against it.
    private static PresentationEvidence EvidenceOf((StoredEmailId Message, PresentationCitationId Citation)? source) =>
        source is { } read
            ? new PresentationEvidence(PresentationSupport.Cited, [read.Citation], PresentationFreshness.Unknown)
            : PresentationEvidence.Unsupported(PresentationFreshness.Unknown);

    /// <summary>Says that a message the model named was not one this run had read, so that the missing source is not silent.</summary>
    private static string UncitedNote(string? messageId, (StoredEmailId Message, PresentationCitationId Citation)? source) =>
        !string.IsNullOrWhiteSpace(messageId) && source is null
            ? " The message you named is not one this conversation has shown you, so the proposal cites no source."
            : string.Empty;

    private static List<EmailAddress>? AddressesOf(IReadOnlyList<string> written)
    {
        List<EmailAddress> addresses = [];

        foreach (var address in written)
        {
            if (!EmailAddress.TryCreate(null, address, out var parsed))
            {
                return null;
            }

            addresses.Add(parsed);
        }

        var distinct = addresses.DistinctBy(static address => address.Address, StringComparer.OrdinalIgnoreCase).ToList();

        return distinct.Count is > 0 and <= DraftBlock.MaxRecipients ? distinct : null;
    }

    private static EmailAddress? SenderOf(ReadEmailContent message) =>
        message.Headers.Participants.FirstOrDefault(static participant => participant.Role is EmailAddressRole.From)?.Address;

    /// <summary>Reads the stretch of one conversation a reading shows, in the conversation's order.</summary>
    /// <remarks>
    /// <para>
    /// The stretch is the conversation's newest <see cref="MaximumThreadMessages" />, because what arrived last is what a
    /// question about a conversation is most often about, and the content read answers a named conversation from its
    /// oldest message. Where the id names a message older than that stretch, the stretch ends at it instead, so the
    /// message asked about is read with what led to it. Whatever falls outside is counted rather than dropped in silence,
    /// so a model reading part of a long conversation knows it has.
    /// </para>
    /// <para>
    /// The content read names the messages it did not serve, so the stretch is read from those identities a bounded
    /// read at a time, and the messages the first read already served are not read again.
    /// </para>
    /// <para>
    /// The order comes from one assembly of the conversation, which stops at
    /// <see cref="IEmailThreadReader.MaximumAssembledEmails" /> messages and does not say whether it stopped early. A
    /// conversation past that is read from its assembled part only: the stretch ends at the last assembled message
    /// rather than the newest, and the count of later messages leaves the unassembled ones out. The tool description
    /// says so, because nothing in the reading can.
    /// </para>
    /// </remarks>
    private async Task<ShownConversation?> ReadConversationAsync(string messageId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(messageId, out var parsed))
        {
            return null;
        }

        var named = StoredEmailId.Create(parsed);
        var opened = await this.readers.ContentReader.ReadContentAsync(
            GetEmailContentRequest.Create([named]) with { Reader = MailboxReaderKind.ConversationAgent },
            cancellationToken);
        var message = opened.Emails.Count is 0 ? null : opened.Emails[0].Content;

        if (message is { Thread: null })
        {
            return new ShownConversation([message], ThreadId: null, EarlierLeftOut: 0, LaterLeftOut: 0);
        }

        // The id may name the conversation itself, which is how the turn names the one the person is looking at.
        var thread = message?.Thread?.ThreadId ?? EmailThreadId.Create(parsed);
        var conversation = await this.readers.ContentReader.ReadContentAsync(
            GetEmailContentRequest.CreateForThread(thread) with { Reader = MailboxReaderKind.ConversationAgent },
            cancellationToken);
        var read = ContentsOf(conversation).ToDictionary(static content => content.StoredEmailId);
        StoredEmailId[] order =
        [
            .. conversation.Emails.Select(static outcome => outcome.StoredEmailId),
            .. conversation.UnreadThreadEmails,
        ];

        var namedAt = message is null ? -1 : Array.IndexOf(order, named);
        var end = namedAt >= 0 && namedAt < order.Length - MaximumThreadMessages ? namedAt : order.Length - 1;
        var start = Math.Max(0, end - MaximumThreadMessages + 1);
        var shown = order[start..(end + 1)];

        var unreadBatches = shown
            .Where(storedEmailId => !read.ContainsKey(storedEmailId))
            .Chunk(GetEmailContentRequest.MaximumEmails)
            .ToArray();

        foreach (var unread in unreadBatches)
        {
            var more = await this.readers.ContentReader.ReadContentAsync(
                GetEmailContentRequest.Create(unread) with { Reader = MailboxReaderKind.ConversationAgent },
                cancellationToken);

            foreach (var content in ContentsOf(more))
            {
                read[content.StoredEmailId] = content;
            }
        }

        ReadEmailContent[] messages = [.. shown.Where(read.ContainsKey).Select(storedEmailId => read[storedEmailId])];

        return new ShownConversation(
            messages,
            messages.FirstOrDefault()?.Thread?.ThreadId,
            EarlierLeftOut: start,
            LaterLeftOut: order.Length - 1 - end);
    }

    private static IEnumerable<ReadEmailContent> ContentsOf(GetEmailContentResult result) =>
        result.Emails.Select(static outcome => outcome.Content).OfType<ReadEmailContent>();

    private async Task<ThreadStateBlock?> ThreadStateBlockOfAsync(
        EmailThreadState state,
        IReadOnlyList<ReadEmailContent> messages,
        CancellationToken cancellationToken)
    {
        var participants = messages
            .Select(SenderOf)
            .OfType<EmailAddress>()
            .DistinctBy(static address => address.Address, StringComparer.OrdinalIgnoreCase)
            .Take(ThreadStateBlock.MaxParticipants)
            .Select(static address => new ThreadParticipant(
                PresentationText.Create(string.IsNullOrWhiteSpace(address.DisplayName) ? address.Address : address.DisplayName),
                address))
            .ToList();
        var subjects = messages.ToDictionary(static message => message.StoredEmailId, static message => message.Headers.Subject);
        List<ThreadStatement> agreements = [];
        List<ThreadStatement> openQuestions = [];
        List<ThreadCommitment> commitments = [];
        HashSet<PresentationCitationId> sources = [];

        foreach (var entry in state.Entries)
        {
            List<PresentationCitationId> cites = [];

            foreach (var source in entry.Sources)
            {
                cites.Add((await this.CiteAsync(source, subjects.GetValueOrDefault(source), cancellationToken)).Id);
            }

            sources.UnionWith(cites);

            switch (entry.Aspect)
            {
                case ThreadStateAspect.Agreement:
                    agreements.Add(new ThreadStatement(PresentationText.Create(entry.Text), cites));
                    break;
                case ThreadStateAspect.OpenQuestion:
                    openQuestions.Add(new ThreadStatement(PresentationText.Create(entry.Text), cites));
                    break;
                case ThreadStateAspect.Commitment:
                    commitments.Add(new ThreadCommitment(
                        PresentationText.Create(entry.Text),
                        entry.OwedBy is { } owedBy ? new ThreadParticipant(PresentationText.Create(owedBy), address: null) : null,
                        entry.DueAt,
                        cites));
                    break;
                default:
                    break;
            }
        }

        if (participants.Count is 0 || agreements.Count + openQuestions.Count + commitments.Count is 0)
        {
            return null;
        }

        var freshness = state.IsCurrent
            ? PresentationFreshness.CurrentAt(state.DerivedAt)
            : PresentationFreshness.StaleSince(state.DerivedAt);
        var evidence = sources.Count is 0
            ? PresentationEvidence.Unsupported(freshness)
            : new PresentationEvidence(PresentationSupport.Supported, [.. sources.Take(PresentationEvidence.MaxCitations)], freshness);

        return new ThreadStateBlock(evidence, participants, agreements, openQuestions, commitments);
    }

    private async Task<PresentationCitation> CiteAsync(StoredEmailId message, string? subject, CancellationToken cancellationToken)
    {
        if (this.cited.TryGetValue(message, out var known))
        {
            return known;
        }

        var citation = new PresentationCitation(
            PresentationCitationId.Create(string.Create(CultureInfo.InvariantCulture, $"m{this.cited.Count + 1}")),
            new EmailCitationTarget(message),
            PresentationText.TryCreate(subject, out var titled) ? titled : PresentationText.Create(message.ToString()),
            PresentationSourceMedium.Written);

        this.cited[message] = citation;
        await this.WriteAsync(this.journal.DeclareAsync(citation, cancellationToken));

        return citation;
    }

    private Task ReportAsync(AgentActivity activity, CancellationToken cancellationToken) =>
        this.WriteAsync(this.journal.ReportAsync(activity, cancellationToken));

    /// <summary>Ends the run where the store refused a write, which is a stop the person recorded.</summary>
    private async Task WriteAsync(Task<bool> write)
    {
        if (!await write)
        {
            this.journal.Stopping.ThrowIfCancellationRequested();
        }
    }

    /// <summary>The messages of one conversation a reading shows, and how many it left out on either side of them.</summary>
    /// <param name="Messages">The messages shown, in the conversation's order.</param>
    /// <param name="ThreadId">The conversation as its messages name it, which a reading may be repeated with to reach its newest messages, or <see langword="null" /> where nothing shown belongs to one.</param>
    /// <param name="EarlierLeftOut">How many of the conversation's messages come before the first one shown.</param>
    /// <param name="LaterLeftOut">How many of the conversation's messages come after the last one shown.</param>
    private sealed record ShownConversation(
        IReadOnlyList<ReadEmailContent> Messages,
        EmailThreadId? ThreadId,
        int EarlierLeftOut,
        int LaterLeftOut);

    /// <summary>A tool that says what the run is doing before it does it.</summary>
    private sealed class ReportingFunction(AIFunction inner, AgentAnswerJournal journal, AgentActivity activity) : DelegatingAIFunction(inner)
    {
        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            if (!await journal.ReportAsync(activity, cancellationToken))
            {
                journal.Stopping.ThrowIfCancellationRequested();
            }

            return await base.InvokeCoreAsync(arguments, cancellationToken);
        }
    }
}
