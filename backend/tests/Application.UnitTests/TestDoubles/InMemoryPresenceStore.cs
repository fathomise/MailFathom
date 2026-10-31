// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Presence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Presence;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Keeps members' open clients and choices under the rules the persisted store publishes: the expiry, the bound, and the last-seen time moving only with a renewal.</summary>
/// <remarks>What it does not model is two replicas writing one member at once, which the persisted store's single statements settle and the integration suite proves.</remarks>
internal sealed class InMemoryPresenceStore : IPresenceStore
{
    private readonly Dictionary<(UserId Member, Guid Client), OpenPresenceClient> clients = [];

    private readonly Dictionary<UserId, PresenceChoice?> choices = [];

    private readonly Dictionary<UserId, DateTimeOffset> lastSeen = [];

    private readonly Dictionary<Guid, IReadOnlyList<UserId>> organizations = [];

    /// <summary>Gets how many sweeps of expired clients were asked for.</summary>
    public int Sweeps { get; private set; }

    /// <summary>States who belongs to an organization.</summary>
    /// <param name="organization">The organization.</param>
    /// <param name="users">Its users.</param>
    /// <returns>This store.</returns>
    public InMemoryPresenceStore WithOrganization(Guid organization, params UserId[] users)
    {
        this.organizations[organization] = users;

        return this;
    }

    /// <summary>Holds one open client for a member, as a renewal left it.</summary>
    /// <param name="member">The member.</param>
    /// <param name="client">The client's identifier.</param>
    /// <param name="activity">Its activity.</param>
    /// <param name="expiresAt">When it stops counting.</param>
    /// <returns>This store.</returns>
    public InMemoryPresenceStore WithClient(UserId member, Guid client, ClientActivity activity, DateTimeOffset expiresAt)
    {
        this.clients[(member, client)] = new OpenPresenceClient(activity, expiresAt);

        return this;
    }

    /// <summary>Holds a member's choice.</summary>
    /// <param name="member">The member.</param>
    /// <param name="choice">The choice.</param>
    /// <returns>This store.</returns>
    public InMemoryPresenceStore WithChoice(UserId member, PresenceChoice choice)
    {
        this.choices[member] = choice;

        return this;
    }

    /// <summary>Holds when a member was last seen.</summary>
    /// <param name="member">The member.</param>
    /// <param name="seen">The time.</param>
    /// <returns>This store.</returns>
    public InMemoryPresenceStore WithLastSeen(UserId member, DateTimeOffset seen)
    {
        this.lastSeen[member] = seen;

        return this;
    }

    /// <summary>Reads what is held about one member.</summary>
    /// <param name="member">The member.</param>
    /// <returns>Their presence inputs.</returns>
    public MemberPresence Of(UserId member) =>
        new(
            [.. this.clients.Where(entry => entry.Key.Member == member).Select(entry => entry.Value)],
            this.choices.GetValueOrDefault(member),
            this.lastSeen.TryGetValue(member, out var seen) ? seen : null);

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<UserId, MemberPresence>> ReadAsync(
        IReadOnlyCollection<UserId> members,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<UserId, MemberPresence>>(members.Distinct().ToDictionary(member => member, this.Of));

    /// <inheritdoc />
    public Task<bool> RenewClientAsync(
        UserId member,
        Guid client,
        ClientActivity activity,
        DateTimeOffset now,
        DateTimeOffset? lastSeen,
        CancellationToken cancellationToken)
    {
        foreach (var expired in this.clients.Where(entry => entry.Key.Member == member && entry.Value.ExpiresAt <= now).ToArray())
        {
            this.clients.Remove(expired.Key);
        }

        var held = this.clients.ContainsKey((member, client));

        if (!held && this.clients.Keys.Count(key => key.Member == member) >= MemberPresence.MostOpenClients)
        {
            return Task.FromResult(false);
        }

        this.clients[(member, client)] = new OpenPresenceClient(activity, now + MemberPresence.ClientLifetime);

        if (lastSeen is { } seen)
        {
            this.lastSeen[member] = seen;
        }

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task RemoveClientAsync(UserId member, Guid client, CancellationToken cancellationToken)
    {
        this.clients.Remove((member, client));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ChooseAsync(UserId member, PresenceChoice? choice, CancellationToken cancellationToken)
    {
        this.choices[member] = choice;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveExpiredClientsAsync(DateTimeOffset removableFrom, int mostRemoved, CancellationToken cancellationToken)
    {
        this.Sweeps++;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<UserId>> ReadOrganizationUsersAsync(Guid organization, CancellationToken cancellationToken) =>
        Task.FromResult(this.organizations.GetValueOrDefault(organization) ?? (IReadOnlyList<UserId>)[]);
}
