// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Presence;

namespace MailFathom.Application.Presence;

/// <summary>Where the deployment holds what members' presence is worked out from: a row per open client, and one row per member.</summary>
/// <remarks>
/// <para>
/// Rows rather than any process's memory, because the replica a client renews against is not the replica a colleague
/// reads on, and a replica that dies has to stop counting as anybody's presence without anything being written for it.
/// A client row therefore carries its own expiry and every read leaves an expired row out, so nothing has to run for a
/// row to stop counting.
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0045-a-members-presence-from-the-clients-it-keeps-open-and-the-state-it-chose.md">ADR 0045</see>
/// decides it.
/// </para>
/// <para>
/// Only the present is held: each row is overwritten by the next write and nothing keeps what it said before. Every
/// value is personal data about the member, and nothing that reads this store logs any of it.
/// </para>
/// </remarks>
public interface IPresenceStore
{
    /// <summary>The most members one read names.</summary>
    /// <remarks>The member directory composes at most a thousand members, so a read the size of the directory is the largest any caller asks.</remarks>
    const int MostReadMembers = 1_000;

    /// <summary>Reads what several members' presence is worked out from.</summary>
    /// <param name="members">The members to read, at most <see cref="MostReadMembers" /> of them.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One entry per member asked for, <see cref="MemberPresence.Unknown" /> for one nothing is held about.</returns>
    Task<IReadOnlyDictionary<UserId, MemberPresence>> ReadAsync(
        IReadOnlyCollection<UserId> members,
        CancellationToken cancellationToken);

    /// <summary>Renews one open client's row, writing it when it is new, and moves the member's last-seen time.</summary>
    /// <param name="member">The member the client belongs to.</param>
    /// <param name="client">The identifier the client minted for itself.</param>
    /// <param name="activity">Whether a person is using the client.</param>
    /// <param name="now">The time of the renewal, which is what the member's own expired rows are judged against.</param>
    /// <param name="lastSeen">The last-seen time to record, or <see langword="null" /> to leave it where it stands.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="false" /> where the client is new and the member already keeps <see cref="MemberPresence.MostOpenClients" /> open, in which case nothing was written.</returns>
    /// <remarks>
    /// The row expires <see cref="MemberPresence.ClientLifetime" /> after <paramref name="now" />. The member's own
    /// expired rows are removed in the same write, so they never count towards the bound.
    /// </remarks>
    Task<bool> RenewClientAsync(
        UserId member,
        Guid client,
        ClientActivity activity,
        DateTimeOffset now,
        DateTimeOffset? lastSeen,
        CancellationToken cancellationToken);

    /// <summary>Removes one client's row, which is what closing it does.</summary>
    /// <param name="member">The member the client belongs to.</param>
    /// <param name="client">The identifier the client minted for itself.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the row is gone; removing a row that is not there is not an error.</returns>
    Task RemoveClientAsync(UserId member, Guid client, CancellationToken cancellationToken);

    /// <summary>Records the presence a member chose, over whatever they chose before.</summary>
    /// <param name="member">The member who chose.</param>
    /// <param name="choice">The choice, or <see langword="null" /> to return them to automatic presence.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the choice is recorded.</returns>
    Task ChooseAsync(UserId member, PresenceChoice? choice, CancellationToken cancellationToken);

    /// <summary>Removes client rows that expired, across the deployment, up to a bound.</summary>
    /// <param name="removableFrom">The instant a row expiring at or before counts as gone.</param>
    /// <param name="mostRemoved">The most rows one call removes.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the batch is removed.</returns>
    /// <remarks>Housekeeping rather than correctness: an expired row already counts for nothing, so a sweep that never runs leaves rows behind and changes nobody's presence.</remarks>
    Task RemoveExpiredClientsAsync(DateTimeOffset removableFrom, int mostRemoved, CancellationToken cancellationToken);

    /// <summary>Reads the persons of an organization, which is who a change to one member's presence is announced to.</summary>
    /// <param name="organization">The organization.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The persons of the organization, at most <see cref="MostReadMembers" /> of them by identifier — the population and the bound the member directory lists, so nobody the directory shows is left untold.</returns>
    /// <remarks>A service account is left out: it appears in no directory, so it has no colleague's presence to redraw.</remarks>
    Task<IReadOnlyList<UserId>> ReadOrganizationUsersAsync(Guid organization, CancellationToken cancellationToken);
}
