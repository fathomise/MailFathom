// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Contacts.Photos;
using MailFathom.Application.Emails.BrowseThreadList;
using MailFathom.Application.Emails.BrowseTimeline;
using MailFathom.Application.Emails.Enrichment;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves one page of the user's message list, keyset-paged in both directions.</summary>
/// <remarks>
/// <para>
/// It is the route a mail screen spends its time in, and the one most easily made slow. A page is an indexed read of a
/// bounded window rather than an offset into a count, so message forty thousand of a folder costs what message one
/// costs, and the cursor that continues the walk is a value the client holds rather than a position this deployment
/// remembers — which is what makes leaving a screen and coming back a continuation instead of a fresh start.
/// </para>
/// <para>
/// A row carries what a list draws and stops there: who wrote it, what it is about, when it arrived, whether it has
/// been read, flagged or answered, whether anything is attached, and the opening of the message's own text. It carries
/// no body and no raw MIME, because a page of fifty rows carrying bodies is a megabyte to draw a list.
/// </para>
/// <para>
/// Both the order the list is sorted in and the direction a page continues in are stated by the request, and a value
/// neither of them accepts is refused rather than ignored: a screen that asked to be sorted by something this
/// deployment cannot sort by would otherwise be handed the default order and no way to tell.
/// </para>
/// <para>
/// It speaks to no mail server, so a request from a browser cannot wait on IMAP and cannot set the remote <c>\Seen</c>
/// flag. What it answers is the local copy, whose currency the folders route is where a screen reads.
/// </para>
/// </remarks>
internal static class ClientMailTimelineEndpoint
{
    /// <summary>The route reporting one page of the user's mail, relative to the client prefix.</summary>
    internal const string MailTimelineRoute = "/emails";

    /// <summary>The one value the <c>sort</c> parameter accepts, which is the column the timeline indexes are ordered by.</summary>
    internal const string ReceivedAtSort = "receivedAt";

    /// <summary>The <c>order</c> value that reads the newest mail first, which is what a request naming none takes.</summary>
    internal const string NewestFirstOrder = "newestFirst";

    /// <summary>The <c>order</c> value that reads the oldest mail first.</summary>
    internal const string OldestFirstOrder = "oldestFirst";

    /// <summary>The <c>grouping</c> value that makes each row one message, which is what a request naming none takes.</summary>
    internal const string MessageGrouping = "message";

    /// <summary>The <c>grouping</c> value that makes each row one conversation, standing at its newest message the read let through.</summary>
    internal const string ConversationGrouping = "conversation";

    /// <summary>The <c>direction</c> value that reads the page after the cursor, which is what a request naming none takes.</summary>
    internal const string ForwardDirection = "forward";

    /// <summary>The <c>direction</c> value that reads the page before the cursor.</summary>
    internal const string BackwardDirection = "backward";

    // The three readings a request may narrow by, spelled as this surface already publishes them on a row's own
    // `enrichment.marks`. A caller therefore hands back the value it read rather than translating between two
    // spellings of one closed set, which is what a second spelling on the same route would have made it do.

    /// <summary>The <c>carriesMark</c> value naming the reading that says what a message is about.</summary>
    internal const string SenseMark = "Sense";

    /// <summary>The <c>carriesMark</c> value naming the reading that says why a message may matter.</summary>
    internal const string SignificanceMark = "Significance";

    /// <summary>The <c>carriesMark</c> value naming the reading that carries a commitment, which is the one that may carry a date.</summary>
    internal const string CommitmentMark = "Commitment";

    /// <summary>Maps the route into the client group, so it inherits the group's requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientMailTimeline(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(MailTimelineRoute, ReadTimelineAsync)
            .RequirePermission(MailFathomPermission.MailRead);
    }

    /// <summary>Serves one page of the acting user's mail, or reports what was wrong with the request.</summary>
    /// <param name="account">The account to draw from, by its identifier or its display name, or <see langword="null" /> for every account the user is assigned.</param>
    /// <param name="folder">The folder to draw from, by the identity the folders route reports or as <c>role:Inbox</c>, or <see langword="null" /> for every folder.</param>
    /// <param name="includeJunk">Whether the junk folder takes part, which it does not unless the request asks.</param>
    /// <param name="unread">Whether to keep only unread mail, only read mail, or <see langword="null" /> for both.</param>
    /// <param name="flagged">Whether to keep only flagged mail, only unflagged mail, or <see langword="null" /> for both.</param>
    /// <param name="hasAttachments">Whether to keep only mail with attachments, only mail without, or <see langword="null" /> for both.</param>
    /// <param name="receivedOnOrAfter">The inclusive start of the received range, or <see langword="null" /> for no start.</param>
    /// <param name="receivedBefore">The exclusive end of the received range, or <see langword="null" /> for no end.</param>
    /// <param name="sort">What the list is ordered by, which is <c>receivedAt</c> or nothing else.</param>
    /// <param name="order">Which end of that order leads, <c>newestFirst</c> or <c>oldestFirst</c>.</param>
    /// <param name="direction">Whether the page lies <c>forward</c> of the cursor or <c>backward</c> of it.</param>
    /// <param name="pageSize">How many rows the page may hold, or <see langword="null" /> for the default.</param>
    /// <param name="cursor">The cursor a previous page returned, or <see langword="null" /> for the leading end of the list.</param>
    /// <param name="carriesMark">The reading a derivation must have made about the message, or <see langword="null" /> for any.</param>
    /// <param name="markDueOnOrAfter">The inclusive start of the range a commitment falls due in, or <see langword="null" /> for no start.</param>
    /// <param name="markDueBefore">The exclusive end of that range, or <see langword="null" /> for no end.</param>
    /// <param name="grouping">Whether a row is a <c>message</c> or a <c>conversation</c>, reading by message where the request names neither.</param>
    /// <param name="timeline">Reads the page by message, for a caller the read's own grant admits.</param>
    /// <param name="conversations">Reads the page grouped by conversation, for a caller the read's own grant admits.</param>
    /// <param name="photos">Resolves the photo each sender on the page is drawn by, for a caller who may read the books.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the page, <c>400</c> naming what was wrong with the request, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.read</c>.</returns>
    /// <remarks>
    /// <para>
    /// Every refusal is <c>400</c> and each one says which of them it is, because a cursor a deployment never issued and
    /// a cursor issued for a different list are two different mistakes with two different repairs — and answering
    /// either with the leading page would be a screen silently jumping back to the top.
    /// </para>
    /// <para>
    /// Grouping changes what a row is and nothing about which mail the filters let through: the same scope, filters,
    /// order, and cursors, with one row kept per conversation. A conversation has no folder, so naming one beside the
    /// grouping is refused rather than ignored, and so is asking for junk mail, which a conversation counts whole anyway.
    /// </para>
    /// </remarks>
    internal static async Task<Results<Ok<ClientMailTimelineResponse>, Ok<ClientMailConversationPageResponse>, ProblemHttpResult>> ReadTimelineAsync(
        [FromQuery] string? account,
        [FromQuery] string? folder,
        [FromQuery] bool? includeJunk,
        [FromQuery] bool? unread,
        [FromQuery] bool? flagged,
        [FromQuery] bool? hasAttachments,
        [FromQuery] DateTimeOffset? receivedOnOrAfter,
        [FromQuery] DateTimeOffset? receivedBefore,
        [FromQuery] string? sort,
        [FromQuery] string? order,
        [FromQuery] string? direction,
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromQuery] string? carriesMark,
        [FromQuery] DateTimeOffset? markDueOnOrAfter,
        [FromQuery] DateTimeOffset? markDueBefore,
        [FromQuery] string? grouping,
        [FromServices] MailTimelineBrowser timeline,
        [FromServices] MailThreadListBrowser conversations,
        [FromServices] PersonPhotos photos,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(conversations);
        ArgumentNullException.ThrowIfNull(photos);

        if (GroupedByConversation(Named(grouping)) is not { } groupedByConversation)
        {
            return Refuse($"The grouping names neither '{MessageGrouping}' nor '{ConversationGrouping}'.");
        }

        if (Named(sort) is { } namedSort && !string.Equals(namedSort, ReceivedAtSort, StringComparison.OrdinalIgnoreCase))
        {
            return Refuse($"A mail list is ordered by '{ReceivedAtSort}', and by nothing else this deployment can index.");
        }

        if (SortedOrder(Named(order)) is not { } sortedOrder)
        {
            return Refuse($"The order names neither '{NewestFirstOrder}' nor '{OldestFirstOrder}'.");
        }

        if (PageDirection(Named(direction)) is not { } pageDirection)
        {
            return Refuse($"The direction names neither '{ForwardDirection}' nor '{BackwardDirection}'.");
        }

        if (!TryReadScope(Named(account), Named(folder), out var accounts, out var folders))
        {
            return Refuse("The account or the folder names a value this deployment does not issue.");
        }

        if (!TryReadMarkAspect(Named(carriesMark), out var carriedAspect))
        {
            return Refuse($"A mark names one of '{SenseMark}', '{SignificanceMark}', and '{CommitmentMark}'.");
        }

        var request = new BrowseTimelineRequest
        {
            Accounts = accounts,
            Folders = folders,
            IncludeJunkMail = includeJunk ?? false,
            IsRemotelySeen = unread is { } wanted ? !wanted : null,
            IsRemotelyFlagged = flagged,
            HasAttachments = hasAttachments,
            ReceivedOnOrAfter = receivedOnOrAfter,
            ReceivedBefore = receivedBefore,
            Order = sortedOrder,
            PageDirection = pageDirection,
            PageSize = pageSize,
            Cursor = cursor,
            CarriesMarkOfAspect = carriedAspect,
            MarkDueOnOrAfter = markDueOnOrAfter,
            MarkDueBefore = markDueBefore,
        };

        try
        {
            if (groupedByConversation)
            {
                var grouped = await conversations.BrowseGroupedPageAsync(request, cancellationToken);
                var authorPhotos = await photos.ResolveAsync(
                    PersonPhotoResponse.AddressesOf(grouped.Conversations.Select(row => row.Newest.SenderAddress)),
                    cancellationToken);

                return TypedResults.Ok(ClientMailConversationPageResponse.For(grouped, authorPhotos));
            }

            var page = await timeline.BrowsePageAsync(request, cancellationToken);
            var senderPhotos = await photos.ResolveAsync(
                PersonPhotoResponse.AddressesOf(page.Emails.Select(row => row.Email.SenderAddress)),
                cancellationToken);

            return TypedResults.Ok(ClientMailTimelineResponse.For(page, senderPhotos));
        }
        catch (MailboxQueryCursorMalformedException)
        {
            return Refuse("The cursor is not one this deployment issued.");
        }
        catch (MailboxQueryCursorFilterMismatchException)
        {
            return Refuse("The cursor was issued for a different list, so the filters or the order have to be the ones it was taken under.");
        }
        catch (MailboxQueryPageSizeOutOfRangeException)
        {
            return Refuse($"A page holds between 1 and {MailboxQueryPageSize.MaximumValue} rows.");
        }
        catch (MailAccountNotAccessibleException)
        {
            return Refuse("The account is not one this user is assigned.");
        }
        catch (MailboxQueryFilterInvalidException refusal)
        {
            return Refuse(refusal.Message);
        }
    }

    /// <summary>States what a caller has to change, without echoing what they sent.</summary>
    private static ProblemHttpResult Refuse(string stated) =>
        TypedResults.Problem(stated, statusCode: StatusCodes.Status400BadRequest);

    /// <summary>Reads a parameter a caller may have sent empty as the parameter they did not send.</summary>
    /// <remarks>
    /// A query string is composed by a page rather than typed, so a field the screen has nothing to put in yet arrives
    /// as <c>?folder=</c> rather than absent. Refusing that would make the two spellings of "no folder" mean different
    /// things for no reason a client could act on, which is how the cursor is already read.
    /// </remarks>
    private static string? Named(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Reads which end of the timeline leads, taking the newest first where the request named nothing.</summary>
    /// <remarks>
    /// A closed mapping rather than <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)" />, which would also
    /// accept a number and a comma-separated list — neither of which any client wrote and both of which name a member.
    /// Case is not part of the name, the way it is not part of a folder role.
    /// </remarks>
    private static EmailTimelineDirection? SortedOrder(string? order) => order switch
    {
        null => EmailTimelineDirection.NewestFirst,
        _ when NamesTheSame(order, NewestFirstOrder) => EmailTimelineDirection.NewestFirst,
        _ when NamesTheSame(order, OldestFirstOrder) => EmailTimelineDirection.OldestFirst,
        _ => null,
    };

    /// <summary>Reads whether a row is a conversation rather than a message, reading by message where the request named nothing.</summary>
    private static bool? GroupedByConversation(string? grouping) => grouping switch
    {
        null => false,
        _ when NamesTheSame(grouping, MessageGrouping) => false,
        _ when NamesTheSame(grouping, ConversationGrouping) => true,
        _ => null,
    };

    /// <summary>Reads which way the page continues from its cursor, continuing forward where the request named nothing.</summary>
    private static TimelinePageDirection? PageDirection(string? direction) => direction switch
    {
        null => TimelinePageDirection.Forward,
        _ when NamesTheSame(direction, ForwardDirection) => TimelinePageDirection.Forward,
        _ when NamesTheSame(direction, BackwardDirection) => TimelinePageDirection.Backward,
        _ => null,
    };

    /// <summary>Reads the reading a derivation must have made, reporting whether the request named one this surface publishes.</summary>
    /// <param name="carriesMark">The mark named, or <see langword="null" /> where the request narrowed by none.</param>
    /// <param name="aspect">The reading named, or <see langword="null" /> where the request narrowed by none.</param>
    /// <returns>Whether the request is one this route can answer.</returns>
    /// <remarks>
    /// Two answers rather than one, because absence and a name nobody publishes are different requests: a list narrowed
    /// by no mark is the ordinary list, and a list narrowed by a word this deployment never issued is a screen asking
    /// for something it will not get — answering the second with the first would draw the whole mailbox under a filter
    /// somebody believes is in force. A closed mapping rather than parsing the enum, for the reason the order is one.
    /// </remarks>
    private static bool TryReadMarkAspect(string? carriesMark, out EmailEnrichmentAspect? aspect)
    {
        aspect = carriesMark switch
        {
            null => null,
            _ when NamesTheSame(carriesMark, SenseMark) => EmailEnrichmentAspect.Sense,
            _ when NamesTheSame(carriesMark, SignificanceMark) => EmailEnrichmentAspect.Significance,
            _ when NamesTheSame(carriesMark, CommitmentMark) => EmailEnrichmentAspect.Commitment,
            _ => null,
        };

        return carriesMark is null || aspect is not null;
    }

    /// <summary>Reports whether a caller wrote one of this surface's published names.</summary>
    private static bool NamesTheSame(string written, string published) =>
        string.Equals(written, published, StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads the two names a request narrows the list with, refusing text no name of this system is spelled with.</summary>
    /// <remarks>
    /// One account and one folder rather than lists of them, because this route draws a folder somebody is looking at.
    /// A request that names neither draws every folder of every account the user is assigned, which is the unified view.
    /// </remarks>
    private static bool TryReadScope(
        string? account,
        string? folder,
        out IReadOnlyList<MailAccountSelector> accounts,
        out IReadOnlyList<MailFolderReference> folders)
    {
        accounts = [];
        folders = [];

        try
        {
            accounts = account is null ? [] : [MailAccountSelector.Create(account)];
            folders = folder is null ? [] : [MailFolderReference.CreateNamedByCaller(folder)];

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

/// <summary>One page of the user's mail, as the client endpoint serves it.</summary>
/// <param name="Emails">The rows, in the order the request asked the list to be sorted in.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> at the end of the list.</param>
/// <param name="PreviousCursor">The cursor the preceding page is asked with, or <see langword="null" /> at the beginning of the list.</param>
/// <param name="PageSize">How many rows the read ran under, which is what the request asked for or the default it took.</param>
/// <remarks>
/// The two cursors are what makes a list scrollable in both directions from one request. Each is opaque and holdable —
/// it names a row of this page together with the list it was read under, and nothing on the server remembers it — so a
/// client may keep one while the screen is closed and continue from it afterwards. An absent one is that end of the
/// list having been reached, never a hint to try again.
/// </remarks>
internal sealed record ClientMailTimelineResponse(
    IReadOnlyList<ClientMailTimelineEntryResponse> Emails,
    string? NextCursor,
    string? PreviousCursor,
    int PageSize)
{
    /// <summary>Describes one page for the wire.</summary>
    /// <param name="page">The page the use case read.</param>
    /// <param name="senderPhotos">The photo each sender on the page is drawn by, keyed by their address.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static ClientMailTimelineResponse For(
        BrowsedTimelinePage page,
        IReadOnlyDictionary<EmailAddress, PersonPhoto> senderPhotos)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(senderPhotos);

        return new ClientMailTimelineResponse(
            [.. page.Emails.Select(row => ClientMailTimelineEntryResponse.For(row, senderPhotos))],
            page.NextCursor,
            page.PreviousCursor,
            page.PageSize);
    }
}

/// <summary>One page of the user's mail grouped by conversation, as the client endpoint serves it.</summary>
/// <param name="Conversations">The conversations, in the order the request asked the list to be sorted in, each standing at its newest message the read let through.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> at the end of the list.</param>
/// <param name="PreviousCursor">The cursor the preceding page is asked with, or <see langword="null" /> at the beginning of the list.</param>
/// <param name="PageSize">How many conversations the read ran under, which is what the request asked for or the default it took.</param>
/// <remarks>
/// A row is the conversation list's row, because it describes the same thing: the whole conversation, whichever folders
/// hold its messages and whichever of them the filters let through. The cursors are held and handed back exactly as a
/// message page's are, and belong to the grouped list alone.
/// </remarks>
internal sealed record ClientMailConversationPageResponse(
    IReadOnlyList<ClientMailThreadListEntryResponse> Conversations,
    string? NextCursor,
    string? PreviousCursor,
    int PageSize)
{
    /// <summary>Describes one page for the wire.</summary>
    /// <param name="page">The page the use case read.</param>
    /// <param name="authorPhotos">The photo each quoted author is drawn by, keyed by their address.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static ClientMailConversationPageResponse For(
        BrowsedConversationPage page,
        IReadOnlyDictionary<EmailAddress, PersonPhoto> authorPhotos)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(authorPhotos);

        return new ClientMailConversationPageResponse(
            [.. page.Conversations.Select(row => ClientMailThreadListEntryResponse.For(row, authorPhotos))],
            page.NextCursor,
            page.PreviousCursor,
            page.PageSize);
    }
}

/// <summary>One row of the list, carrying what a screen draws and nothing it does not.</summary>
/// <param name="Id">The stable local identity of the email, which every later request names it by.</param>
/// <param name="Account">The account the email was read from, as the accounts route names it.</param>
/// <param name="Folder">The identity of the folder the email was read from, as the folders route names it.</param>
/// <param name="ThreadId">The conversation the email belongs to, or <see langword="null" /> where nothing has placed it in one.</param>
/// <param name="Subject">The subject, or <see langword="null" /> where the message carried none.</param>
/// <param name="ReceivedAt">When the last receiving hop recorded the message, which is what the list is ordered by, or <see langword="null" /> where no header carried a usable date.</param>
/// <param name="SentAt">When the message says it was sent, or <see langword="null" /> where no header carried a usable date.</param>
/// <param name="SenderAddress">The sender's address as the message wrote it, or <see langword="null" /> where no usable sender was found.</param>
/// <param name="SenderDisplayName">The display name the sender wrote, or <see langword="null" /> where the header carried none.</param>
/// <param name="ToAddresses">The comparison forms of the <c>To</c> addresses, in header order, which is what a sent-mail row draws instead of a sender.</param>
/// <param name="Unread">Whether the mail server last reported the message without <c>\Seen</c>.</param>
/// <param name="Flagged">Whether the mail server last reported it with <c>\Flagged</c>.</param>
/// <param name="Answered">Whether the mail server last reported it with <c>\Answered</c>.</param>
/// <param name="HasAttachments">Whether the message carries anything besides its body and its inline resources.</param>
/// <param name="AttachmentCount">How many of those there are.</param>
/// <param name="SizeOctets">The size the mail server reported for the message.</param>
/// <param name="Preview">The opening of the message's own text, bounded, or <see langword="null" /> where nothing has extracted the message yet.</param>
/// <param name="Enrichment">What a derivation concluded about the message, or <see langword="null" /> where none has reached it.</param>
/// <param name="ThreadMessageCount">How many messages this user may see in the row's conversation, or <see langword="null" /> where threading has placed the message in none.</param>
/// <param name="Spam">What spam classification concluded about the message, or <see langword="null" /> where nothing has classified it.</param>
/// <param name="Sender">The facts about who sent the message that a row's sender mark is chosen from.</param>
/// <param name="SenderPhoto">The photo the sender is drawn by, or <see langword="null" /> where they are drawn by their initials.</param>
/// <param name="Report">The delivery report or read receipt the message was recognized as, or <see langword="null" /> for every other message.</param>
/// <param name="Receipts">What the account's own message asked to be told and how many recipients each report covered, or <see langword="null" /> where it asked for nothing and nothing came.</param>
/// <remarks>
/// <para>
/// The three flags are published as the states a row draws rather than as the snapshot they came from, because a screen
/// draws an unread badge and not an observation instant. A message no run has read flags for reads as read, unflagged
/// and unanswered, which is what a folder still being backfilled shows and what the folders route's freshness is where
/// a screen tells the two apart.
/// </para>
/// <para>
/// <c>preview</c> is the message's own text and nothing else: no quoted history, no signature block, and never a body.
/// It is absent rather than empty for a message this deployment has stored but not yet extracted.
/// </para>
/// <para>
/// <c>enrichment</c> is absent for a message no derivation has reached — which is every message on a deployment that
/// has not turned enrichment on — and present with an empty <c>marks</c> array for one a derivation answered nothing
/// about. The two are different states and a screen draws them differently: the first may change on a later run and
/// the second will not.
/// </para>
/// <para>
/// <c>threadMessageCount</c> counts the whole correspondence rather than the rows one page holds, and is counted across
/// every folder this user may read with the junk folder included — the same membership the conversation route reads
/// under. It is absent for a message threading has placed in no conversation, and <c>1</c> for one whose conversation
/// holds only it.
/// </para>
/// <para>
/// It is counted rather than assembled, so it is the one place on this surface a conversation's length is published
/// without the assembly bound. The two agree up to
/// <see cref="Application.Emails.Threads.IEmailThreadReader.MaximumAssembledEmails" /> and part above it: a
/// correspondence longer than that is published here as the length it actually is, while the conversation route
/// reports the bound and says so with <c>moreMessagesNotAssembled</c>. That is deliberate rather than a discrepancy to
/// reconcile — what this field is drawn as says *this row stands for a long exchange*, which a number capped at the
/// bound would stop saying.
/// </para>
/// </remarks>
internal sealed record ClientMailTimelineEntryResponse(
    Guid Id,
    string Account,
    string Folder,
    Guid? ThreadId,
    string? Subject,
    DateTimeOffset? ReceivedAt,
    DateTimeOffset? SentAt,
    string? SenderAddress,
    string? SenderDisplayName,
    IReadOnlyList<string> ToAddresses,
    bool Unread,
    bool Flagged,
    bool Answered,
    bool HasAttachments,
    int AttachmentCount,
    long SizeOctets,
    string? Preview,
    ClientMailEnrichmentResponse? Enrichment,
    int? ThreadMessageCount,
    ClientMailSpamVerdictResponse? Spam,
    ClientMailListedSenderResponse Sender,
    PersonPhotoResponse? SenderPhoto,
    ClientMailReportResponse? Report,
    ClientMailReceiptSummaryResponse? Receipts)
{
    /// <summary>Describes one row for the wire.</summary>
    /// <param name="row">The row the use case read.</param>
    /// <param name="senderPhotos">The photo each sender of the read is drawn by, keyed by their address.</param>
    /// <returns>The response body.</returns>
    internal static ClientMailTimelineEntryResponse For(
        BrowsedEmail row,
        IReadOnlyDictionary<EmailAddress, PersonPhoto> senderPhotos) =>
        For(row.Email, row.Preview, row.Enrichment, row.ThreadMessageCount, senderPhotos);

    /// <summary>Describes one message for the wire, wherever on this surface a message is drawn.</summary>
    /// <param name="email">The message the use case read.</param>
    /// <param name="preview">The opening of the message's own text, or <see langword="null" /> where nothing has extracted it.</param>
    /// <param name="enrichment">What a derivation concluded about the message, or <see langword="null" /> where none has reached it.</param>
    /// <param name="threadMessageCount">How many messages the caller may see in the message's conversation, or <see langword="null" /> where it is in none.</param>
    /// <param name="senderPhotos">The photo each sender of the read is drawn by, keyed by their address.</param>
    /// <returns>The response body.</returns>
    /// <remarks>
    /// These rather than a reading's own row type, because a message is one shape on this surface: a list row and a
    /// message inside a conversation are drawn from the same fields, and a second mapping is how the two would come to
    /// publish one message two ways. The derivation, the conversation's size, and the senders' photos are parameters
    /// rather than defaults for the same reason — a caller that had one and did not pass it would publish
    /// <see langword="null" />, which this surface reads as nothing having reached the message, or a sender with no
    /// photo, rather than as this reading not having asked.
    /// </remarks>
    internal static ClientMailTimelineEntryResponse For(
        EmailSummary email,
        string? preview,
        EmailEnrichment? enrichment,
        int? threadMessageCount,
        IReadOnlyDictionary<EmailAddress, PersonPhoto> senderPhotos) => new(
        email.StoredEmailId.Value,
        email.Account.Value,
        email.Folder.Id.ToString(),
        email.ThreadId?.Value,
        email.Subject,
        email.ReceivedAt,
        email.SentAt,
        email.SenderAddress,
        email.SenderDisplayName,
        email.ToAddresses,
        !email.RemoteFlags.IsSeen,
        email.RemoteFlags.IsFlagged,
        email.RemoteFlags.IsAnswered,
        email.Attachments.HasAttachments,
        email.Attachments.AttachmentCount,
        email.SizeOctets,
        preview,
        ClientMailEnrichmentResponse.For(enrichment),
        threadMessageCount,
        ClientMailSpamVerdictResponse.For(email.Spam),
        ClientMailListedSenderResponse.For(email.SenderVerification),
        PersonPhotoResponse.For(email.SenderAddress, senderPhotos),
        ClientMailReportResponse.For(email.Report),
        ClientMailReceiptSummaryResponse.For(email.Receipts));
}

/// <summary>What a row publishes about who sent its message: enough to choose the sender mark, and nothing more.</summary>
/// <param name="AuthorAuthentication">What was established about the displayed author, as the outcome's own name.</param>
/// <param name="DeploymentTrust">Whether this deployment recognizes that author, as the level's own name.</param>
/// <param name="SignatureOutcome">What verifying the message's own signature concluded — <c>NotSigned</c>, <c>Verified</c>, <c>Failed</c>, or <c>NotVerifiable</c>.</param>
/// <param name="SignerMatchesAuthor">Whether the signer is the author the message displays.</param>
/// <remarks>
/// <para>
/// Four facts side by side and never a combined value, for the reason the message route publishes its verdict that
/// way: a mark drawn from them is the client's rule, and a value combining them here would be a second copy of it. The
/// evidence each was reached from — the domains, the DMARC result, the signer — stays on the message route, which is
/// what a details popup opened from the row reads; a list page carrying it would publish what no row draws.
/// </para>
/// <para>
/// Every row carries it, sent mail included, because the facts are the message's; which rows draw a mark is the
/// screen's to decide. They are personal data like the rest of the row.
/// </para>
/// </remarks>
internal sealed record ClientMailListedSenderResponse(
    string AuthorAuthentication,
    string DeploymentTrust,
    string SignatureOutcome,
    bool SignerMatchesAuthor)
{
    /// <summary>Describes one message's sender facts for a row.</summary>
    /// <param name="verification">The conclusions the read carried.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="verification" /> is <see langword="null" />.</exception>
    internal static ClientMailListedSenderResponse For(SenderVerification verification)
    {
        ArgumentNullException.ThrowIfNull(verification);

        return new ClientMailListedSenderResponse(
            verification.AuthorAuthentication.ToString(),
            verification.DeploymentTrust.ToString(),
            verification.Signature.Outcome.ToString(),
            verification.Signature.SignerMatchesAuthor);
    }
}
