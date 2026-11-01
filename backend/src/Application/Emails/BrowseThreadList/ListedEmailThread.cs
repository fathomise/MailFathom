// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.BrowseThreadList;

/// <summary>One conversation as the list reads it out of stored mail, before any of its messages is described.</summary>
/// <remarks>
/// Every count and every message named here is taken from the messages the reader may read, Spam and Trash included,
/// because that is what the conversation holds when it is opened. Spam and Trash decide only whether it is listed.
/// </remarks>
public sealed record ListedEmailThread
{
    /// <summary>Gets the conversation.</summary>
    public required EmailThreadId ThreadId { get; init; }

    /// <summary>Gets when its newest message arrived, which is what the list is ordered by, or <see langword="null" /> where none carried a usable date.</summary>
    public required DateTimeOffset? NewestArrival { get; init; }

    /// <summary>Gets how many messages it holds that the reader may read.</summary>
    public required int MessageCount { get; init; }

    /// <summary>Gets how many of those somebody else sent and the reader has not read yet.</summary>
    public required int UnreadCount { get; init; }

    /// <summary>Gets whether any of its messages carries an attachment.</summary>
    public required bool HasAttachments { get; init; }

    /// <summary>Gets its newest message, which is what a row quotes.</summary>
    public required StoredEmailId NewestEmailId { get; init; }

    /// <summary>Gets its newest message somebody other than the reader sent, or <see langword="null" /> where the reader wrote every one of them.</summary>
    public required StoredEmailId? NewestReceivedEmailId { get; init; }

    /// <summary>Gets who wrote into it, in the order they first did, at most one past <see cref="IEmailThreadListReader.MaximumNamedParticipants" />.</summary>
    public required IReadOnlyList<ListedThreadParticipant> Participants { get; init; }

    /// <summary>Gets what a derivation called it, or <see langword="null" /> where nothing has titled it.</summary>
    public required ListedThreadTitle? Title { get; init; }

    /// <summary>Gets the position a page ending on this conversation continues after.</summary>
    public EmailThreadListPosition Position => new(this.NewestArrival, this.ThreadId);
}
