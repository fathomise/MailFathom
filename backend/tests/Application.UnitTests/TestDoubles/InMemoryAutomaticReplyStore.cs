// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.AutomaticReplies;
using MailFathom.Domain.Access;
using MailFathom.Domain.AutomaticReplies;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Holds automatic replies and their claimed senders in memory, with the store's one-claim-per-sender rule.</summary>
internal sealed class InMemoryAutomaticReplyStore : IAutomaticReplyStore
{
    private readonly Dictionary<UserId, StoredAutomaticReply> replies = [];
    private readonly HashSet<(UserId User, Guid Revision, string Sender)> claims = [];

    /// <summary>Gets the senders claimed so far, in their comparison form.</summary>
    public IReadOnlyList<string> ClaimedSenders => [.. this.claims.Select(static claim => claim.Sender)];

    /// <summary>Gets or sets whether this store holds the users it is asked to save for.</summary>
    public bool HoldsUsers { get; set; } = true;

    /// <inheritdoc />
    public Task<StoredAutomaticReply?> ReadAsync(UserId user, CancellationToken cancellationToken) =>
        Task.FromResult(this.replies.GetValueOrDefault(user));

    /// <inheritdoc />
    public Task<StoredAutomaticReply?> SaveAsync(UserId user, AutomaticReply reply, CancellationToken cancellationToken)
    {
        if (!this.HoldsUsers)
        {
            return Task.FromResult<StoredAutomaticReply?>(null);
        }

        var saved = new StoredAutomaticReply(reply, Guid.CreateVersion7());
        this.replies[user] = saved;
        this.claims.RemoveWhere(claim => claim.User == user);

        return Task.FromResult<StoredAutomaticReply?>(saved);
    }

    /// <inheritdoc />
    public Task<bool> TryClaimAsync(UserId user, Guid revision, EmailAddress sender, CancellationToken cancellationToken) =>
        Task.FromResult(
            this.replies.TryGetValue(user, out var held)
            && held.Revision == revision
            && this.claims.Add((user, revision, sender.NormalizedAddress)));
}
