// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.AutomaticReplies;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.AutomaticReplies;

/// <summary>Holds each user's automatic reply and the senders it has already answered.</summary>
public interface IAutomaticReplyStore
{
    /// <summary>The most users one read of periods names.</summary>
    /// <remarks>The member directory composes at most a thousand members, so a read the size of the directory is the largest any caller asks.</remarks>
    const int MostReadPeriods = 1_000;

    /// <summary>Reads when several users' automatic replies answer, without what they answer with.</summary>
    /// <param name="users">The users to read, at most <see cref="MostReadPeriods" /> of them.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One entry per user asked for who ever set a reply; a user who never did has none.</returns>
    /// <remarks>Neither text is read: what a colleague is told about somebody's reply is that it runs and until when.</remarks>
    Task<IReadOnlyDictionary<UserId, AutomaticReplyPeriod>> ReadPeriodsAsync(
        IReadOnlyCollection<UserId> users,
        CancellationToken cancellationToken);

    /// <summary>Reads one user's automatic reply.</summary>
    /// <param name="user">The user.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The reply as last saved, or <see langword="null" /> where the user never set one.</returns>
    Task<StoredAutomaticReply?> ReadAsync(UserId user, CancellationToken cancellationToken);

    /// <summary>Replaces one user's automatic reply under a new revision, forgetting every sender the previous one answered.</summary>
    /// <param name="user">The user.</param>
    /// <param name="reply">The whole reply.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The reply as saved, or <see langword="null" /> where this deployment holds no such user.</returns>
    /// <remarks>
    /// The senders are forgotten in the same statement that writes the reply, because they were answered under the old
    /// revision and nothing will ask about them again: keeping them would be keeping correspondents' addresses for no
    /// purpose.
    /// </remarks>
    Task<StoredAutomaticReply?> SaveAsync(UserId user, AutomaticReply reply, CancellationToken cancellationToken);

    /// <summary>Claims the one reply a sender is owed under a revision.</summary>
    /// <param name="user">The user whose reply answers.</param>
    /// <param name="revision">The revision the sender is answered under.</param>
    /// <param name="sender">The address the reply goes to.</param>
    /// <param name="cancellationToken">Cancels the claim.</param>
    /// <returns>
    /// <see langword="true" /> when this call claimed it, and <see langword="false" /> when the sender was already
    /// answered under the revision — by this replica or another one — or the revision is no longer the user's.
    /// </returns>
    /// <remarks>
    /// The claim is a row and the unique key under it is the whole guarantee: two replicas answering two messages from
    /// one sender at once both reach the database, and only one of them inserts.
    /// </remarks>
    Task<bool> TryClaimAsync(UserId user, Guid revision, EmailAddress sender, CancellationToken cancellationToken);
}
