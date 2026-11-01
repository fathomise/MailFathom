// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Contacts.Photos;
using MailFathom.Application.Emails.BrowseThreadList;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves one page of the user's conversations, newest first, which is what a list read by thread draws.</summary>
/// <remarks>
/// <para>
/// The list has no folders. A conversation gathers its messages from every folder the user may read, junk and Trash
/// included, and is listed under the account that holds it — or under all of the user's accounts — while at least one
/// of those messages is neither judged spam nor in Trash. Archive is an ordinary folder.
/// </para>
/// <para>
/// A row carries what a thread row draws and stops there: who wrote into the conversation with the user marked, how
/// many messages it holds and how many of the received ones are unread, whether anything in it is attached, its newest
/// message quoted by author, time, and opening, the facts its newest received message's sender mark is chosen from,
/// and what a derivation called it. Opening it is the conversation route; marking it read is one flag change per
/// unread message on the mutations route, which takes a batch of them in one request.
/// </para>
/// <para>
/// It speaks to no mail server, as the timeline does not.
/// </para>
/// </remarks>
internal static class ClientMailThreadListEndpoint
{
    /// <summary>The route reporting one page of the user's conversations, relative to the client prefix.</summary>
    internal const string MailThreadListRoute = "/threads";

    /// <summary>Maps the route into the client group, so it inherits the group's requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientMailThreadList(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(MailThreadListRoute, ReadThreadsAsync)
            .RequirePermission(MailFathomPermission.MailRead);
    }

    /// <summary>Serves one page of the acting user's conversations, or reports what was wrong with the request.</summary>
    /// <param name="account">The account to list, by its identifier or its display name, or <see langword="null" /> for every account the user is assigned.</param>
    /// <param name="pageSize">How many conversations the page may hold, or <see langword="null" /> for the default.</param>
    /// <param name="cursor">The cursor a previous page returned, or <see langword="null" /> for the newest conversations.</param>
    /// <param name="threads">Reads the page.</param>
    /// <param name="photos">Resolves the photo each quoted author is drawn by, for a caller who may read the books.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the page, <c>400</c> naming what was wrong with the request, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.read</c>.</returns>
    internal static async Task<Results<Ok<ClientMailThreadListResponse>, ProblemHttpResult>> ReadThreadsAsync(
        [FromQuery] string? account,
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromServices] MailThreadListBrowser threads,
        [FromServices] PersonPhotos photos,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(threads);
        ArgumentNullException.ThrowIfNull(photos);

        MailAccountSelector? selector;

        try
        {
            selector = string.IsNullOrWhiteSpace(account) ? null : MailAccountSelector.Create(account.Trim());
        }
        catch (ArgumentException)
        {
            return Refuse("The account names a value this deployment does not issue.");
        }

        var request = new BrowseThreadListRequest
        {
            Account = selector,
            PageSize = pageSize,
            Cursor = string.IsNullOrWhiteSpace(cursor) ? null : cursor.Trim(),
        };

        try
        {
            var page = await threads.BrowsePageAsync(request, cancellationToken);
            var authorPhotos = await photos.ResolveAsync(
                PersonPhotoResponse.AddressesOf(page.Threads.Select(row => row.Newest.SenderAddress)),
                cancellationToken);

            return TypedResults.Ok(ClientMailThreadListResponse.For(page, authorPhotos));
        }
        catch (MailboxQueryCursorMalformedException)
        {
            return Refuse("The cursor is not one this deployment issued.");
        }
        catch (MailboxQueryCursorFilterMismatchException)
        {
            return Refuse("The cursor was issued for a different list, so the account has to be the one it was taken under.");
        }
        catch (MailboxQueryPageSizeOutOfRangeException)
        {
            return Refuse($"A page holds between 1 and {MailboxQueryPageSize.MaximumValue} conversations.");
        }
        catch (MailAccountNotAccessibleException)
        {
            return Refuse("The account is not one this user is assigned.");
        }
    }

    /// <summary>States what a caller has to change, without echoing what they sent.</summary>
    private static ProblemHttpResult Refuse(string stated) =>
        TypedResults.Problem(stated, statusCode: StatusCodes.Status400BadRequest);
}

/// <summary>One page of the user's conversations, as the client endpoint serves it.</summary>
/// <param name="Threads">The conversations, newest first by their newest message.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> at the end of the list.</param>
/// <param name="PageSize">How many conversations the read ran under, which is what the request asked for or the default it took.</param>
internal sealed record ClientMailThreadListResponse(
    IReadOnlyList<ClientMailThreadListEntryResponse> Threads,
    string? NextCursor,
    int PageSize)
{
    /// <summary>Describes one page for the wire.</summary>
    /// <param name="page">The page the use case read.</param>
    /// <param name="authorPhotos">The photo each quoted author is drawn by, keyed by their address.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static ClientMailThreadListResponse For(
        BrowsedThreadList page,
        IReadOnlyDictionary<EmailAddress, PersonPhoto> authorPhotos)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(authorPhotos);

        return new ClientMailThreadListResponse(
            [.. page.Threads.Select(row => ClientMailThreadListEntryResponse.For(row, authorPhotos))],
            page.NextCursor,
            page.PageSize);
    }
}

/// <summary>One row of the conversation list.</summary>
/// <param name="ThreadId">The conversation, which the conversation route opens it by.</param>
/// <param name="Account">The account that holds it, as the accounts route names it.</param>
/// <param name="MessageCount">How many of its messages the user may read, Spam and Trash included.</param>
/// <param name="UnreadCount">How many of those somebody other than the user sent and the user has not read; the conversation is unread while this is above zero.</param>
/// <param name="HasAttachments">Whether any of its messages carries an attachment.</param>
/// <param name="Participants">Who wrote into it, in the order they first did, at most eight.</param>
/// <param name="MoreParticipantsNotNamed">Whether more people wrote into it than are named.</param>
/// <param name="Newest">Its newest message, which the row quotes.</param>
/// <param name="NewestReceivedSender">The facts the sender mark of its newest received message is chosen from, or <see langword="null" /> where the user wrote every message.</param>
/// <param name="Title">What a derivation called it, or <see langword="null" /> where nothing has titled it.</param>
/// <remarks>
/// All of it is personal data drawn from the user's mail, under the same rules as a timeline row.
/// </remarks>
internal sealed record ClientMailThreadListEntryResponse(
    Guid ThreadId,
    string Account,
    int MessageCount,
    int UnreadCount,
    bool HasAttachments,
    IReadOnlyList<ClientMailThreadListParticipantResponse> Participants,
    bool MoreParticipantsNotNamed,
    ClientMailThreadListNewestResponse Newest,
    ClientMailListedSenderResponse? NewestReceivedSender,
    ClientMailThreadListTitleResponse? Title)
{
    /// <summary>Describes one row for the wire.</summary>
    /// <param name="row">The row the use case read.</param>
    /// <param name="authorPhotos">The photo each quoted author is drawn by, keyed by their address.</param>
    /// <returns>The response body.</returns>
    internal static ClientMailThreadListEntryResponse For(
        BrowsedListedThread row,
        IReadOnlyDictionary<EmailAddress, PersonPhoto> authorPhotos) => new(
        row.Thread.ThreadId.Value,
        row.Newest.Account.Value,
        row.Thread.MessageCount,
        row.Thread.UnreadCount,
        row.Thread.HasAttachments,
        [
            .. row.Thread.Participants.Select(static participant => new ClientMailThreadListParticipantResponse(
                participant.Address,
                participant.DisplayName,
                participant.IsReader)),
        ],
        row.MoreParticipantsNotNamed,
        new ClientMailThreadListNewestResponse(
            row.Newest.StoredEmailId.Value,
            row.Newest.Subject,
            row.Newest.ReceivedAt,
            row.Newest.SenderAddress,
            row.Newest.SenderDisplayName,
            row.NewestPreview,
            row.NewestIsReaders,
            PersonPhotoResponse.For(row.Newest.SenderAddress, authorPhotos)),
        row.NewestReceivedSender is { } sender ? ClientMailListedSenderResponse.For(sender) : null,
        row.Thread.Title is { } title
            ? new ClientMailThreadListTitleResponse(
                title.Title.Topic,
                title.Title.Standing,
                title.DerivedFrom.MessageCount,
                title.DerivedFrom.LatestArrival)
            : null);
}

/// <summary>Somebody who wrote into a listed conversation.</summary>
/// <param name="Address">Their address as they last wrote it.</param>
/// <param name="DisplayName">The name they last wrote beside it, or <see langword="null" />.</param>
/// <param name="Reader">Whether the address is one the user sends as, which a row draws as the user rather than by name.</param>
internal sealed record ClientMailThreadListParticipantResponse(string Address, string? DisplayName, bool Reader);

/// <summary>The newest message of a listed conversation, as much of it as a row quotes.</summary>
/// <param name="Id">The message, which every later request names it by.</param>
/// <param name="Subject">Its subject, which a row draws where the conversation has no title, or <see langword="null" /> where it carried none.</param>
/// <param name="ReceivedAt">When it arrived, or <see langword="null" /> where no header carried a usable date.</param>
/// <param name="SenderAddress">Its author's address as the message wrote it, or <see langword="null" />.</param>
/// <param name="SenderDisplayName">The name its author wrote, or <see langword="null" />.</param>
/// <param name="Preview">The opening of its own text, bounded, or <see langword="null" /> where nothing has extracted it yet.</param>
/// <param name="Own">Whether the user wrote it.</param>
/// <param name="SenderPhoto">The photo its author is drawn by, or <see langword="null" /> where they are drawn by their initials.</param>
internal sealed record ClientMailThreadListNewestResponse(
    Guid Id,
    string? Subject,
    DateTimeOffset? ReceivedAt,
    string? SenderAddress,
    string? SenderDisplayName,
    string? Preview,
    bool Own,
    PersonPhotoResponse? SenderPhoto);

/// <summary>What a listed conversation is called, and the shape of the conversation it was derived from.</summary>
/// <param name="Topic">What the conversation is about, kept as replies arrive.</param>
/// <param name="Standing">Where it stands, empty where nothing in it is left open.</param>
/// <param name="DerivedFromMessageCount">How many messages the derivation counted.</param>
/// <param name="DerivedFromLatestArrival">When the newest of them arrived, or <see langword="null" /> where none carried a date.</param>
/// <remarks>The revision is what tells a client the title changed: a reply that arrives since moves it on the next derivation.</remarks>
internal sealed record ClientMailThreadListTitleResponse(
    string Topic,
    string Standing,
    int DerivedFromMessageCount,
    DateTimeOffset? DerivedFromLatestArrival);
