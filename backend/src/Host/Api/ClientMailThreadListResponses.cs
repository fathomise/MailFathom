// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Contacts.Photos;
using MailFathom.Application.Emails.BrowseThreadList;
using MailFathom.Domain.Emails;

namespace MailFathom.Host.Api;

/// <summary>One row of the mail list read by conversation.</summary>
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
/// <param name="Categories">Every category any of those messages holds and its account shows, each once, a default before an own category.</param>
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
    ClientMailThreadListTitleResponse? Title,
    IReadOnlyList<ClientMailCategoryResponse> Categories)
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
            PersonPhotoResponse.For(row.Newest.SenderAddress, authorPhotos),
            row.NewestIsReaders ? ClientMailReceiptSummaryResponse.For(row.Newest.Receipts) : null),
        row.NewestReceivedSender is { } sender ? ClientMailListedSenderResponse.For(sender) : null,
        row.Thread.Title is { } title
            ? new ClientMailThreadListTitleResponse(
                title.Title.Topic,
                title.Title.Standing,
                title.DerivedFrom.MessageCount,
                title.DerivedFrom.LatestArrival)
            : null,
        ClientMailCategoryResponse.For(row.Thread.Categories));
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
/// <param name="Receipts">What came back for it, where the user wrote it and it asked for a report or got one; <see langword="null" /> otherwise, a received newest message always included.</param>
internal sealed record ClientMailThreadListNewestResponse(
    Guid Id,
    string? Subject,
    DateTimeOffset? ReceivedAt,
    string? SenderAddress,
    string? SenderDisplayName,
    string? Preview,
    bool Own,
    PersonPhotoResponse? SenderPhoto,
    ClientMailReceiptSummaryResponse? Receipts);

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
