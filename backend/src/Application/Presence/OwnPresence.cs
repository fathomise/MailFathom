// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Contacts;
using MailFathom.Application.Signals;
using MailFathom.Domain.Access;
using MailFathom.Domain.Presence;

namespace MailFathom.Application.Presence;

/// <summary>The signed-in member's own presence: the clients they keep open, the state they chose, their status message and work mode, and what they read of it.</summary>
/// <remarks>
/// <para>
/// Presence exists inside an organization alone, because nobody else can see it: a user in no organization is answered
/// <see cref="PresenceWriteOutcome.NotInOrganization" /> and nothing is recorded about them.
/// </para>
/// <para>
/// A write that changes what the other members see tells the member's own clients alone, so their other devices re-read
/// the member's own presence. The other members are never told: they read colleagues again on their client's own
/// refresh, which keeps one change at one signal however large the organization. A change no write causes — a client
/// that crashed and expired, a duration that ran out — is not announced either.
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0045-a-members-presence-from-the-clients-it-keeps-open-and-the-state-it-chose.md">ADR 0045</see>
/// decides it.
/// </para>
/// <para>Nothing here logs: a renewal is the most frequent request a client makes, and a line per renewal would be a history of somebody's working day.</para>
/// </remarks>
public sealed class OwnPresence
{
    /// <summary>The most expired client rows one sweep removes.</summary>
    private const int MostSweptClients = 1_000;

    private readonly IPresenceStore store;
    private readonly ClientSignals signals;
    private readonly ContactBookOwnership ownership;
    private readonly AccessAuthorization authorization;
    private readonly IUserTimeZones timeZones;
    private readonly PresenceSweepSchedule sweeps;
    private readonly TimeProvider time;

    /// <summary>Initializes the use case.</summary>
    /// <param name="store">Holds what presence is worked out from.</param>
    /// <param name="signals">Tells the member's own clients that what the others see of them changed.</param>
    /// <param name="ownership">Answers who the caller is and which organization they belong to.</param>
    /// <param name="authorization">Answers what the caller holds.</param>
    /// <param name="timeZones">Answers the zone a duration ends in.</param>
    /// <param name="sweeps">Decides when this process removes expired client rows.</param>
    /// <param name="time">The clock every instant is read from.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public OwnPresence(
        IPresenceStore store,
        ClientSignals signals,
        ContactBookOwnership ownership,
        AccessAuthorization authorization,
        IUserTimeZones timeZones,
        PresenceSweepSchedule sweeps,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(timeZones);
        ArgumentNullException.ThrowIfNull(sweeps);
        ArgumentNullException.ThrowIfNull(time);

        this.store = store;
        this.signals = signals;
        this.ownership = ownership;
        this.authorization = authorization;
        this.timeZones = timeZones;
        this.sweeps = sweeps;
        this.time = time;
    }

    /// <summary>Reads the caller's own presence as they see it, <see cref="PresenceState.Hidden" /> included.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The caller's state and choice, or <see langword="null" /> where they belong to no organization.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the profile reading grant.</exception>
    public async Task<OwnPresenceReading?> ReadAsync(CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.ProfileRead);

        if (await this.ReadMemberAsync(cancellationToken) is not { } member)
        {
            return null;
        }

        var now = this.time.GetUtcNow();
        var presence = await this.ReadAsync(member, cancellationToken);

        return OwnPresenceReading.Of(presence, now);
    }

    /// <summary>Renews one of the caller's open clients, which is what keeps them present.</summary>
    /// <param name="client">The identifier the client minted for itself.</param>
    /// <param name="activity">Whether a person is using it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Whether the renewal was recorded, and why not where it was not.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the profile writing grant.</exception>
    /// <remarks>The last-seen time moves with the renewal, except while the caller chose to look absent.</remarks>
    public async Task<PresenceWriteOutcome> RenewAsync(
        Guid client,
        ClientActivity activity,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.ProfileWrite);

        if (await this.ReadMemberAsync(cancellationToken) is not { } member)
        {
            return PresenceWriteOutcome.NotInOrganization;
        }

        var now = this.time.GetUtcNow();
        var before = await this.ReadAsync(member, cancellationToken);

        if (!await this.store.RenewClientAsync(
                member,
                client,
                activity,
                now,
                before.AdvancesLastSeenAt(now) ? now : null,
                cancellationToken))
        {
            return PresenceWriteOutcome.TooManyClients;
        }

        await this.AnnounceIfSeenDifferentlyAsync(member, before, now, cancellationToken);
        await this.SweepIfDueAsync(now, cancellationToken);

        return PresenceWriteOutcome.Recorded;
    }

    /// <summary>Closes one of the caller's clients, which reads as <see cref="PresenceState.Offline" /> at once when it was their last.</summary>
    /// <param name="client">The identifier the client minted for itself.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Whether the close was recorded; closing a client that was not open is recorded as well.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the profile writing grant.</exception>
    public async Task<PresenceWriteOutcome> CloseAsync(Guid client, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.ProfileWrite);

        if (await this.ReadMemberAsync(cancellationToken) is not { } member)
        {
            return PresenceWriteOutcome.NotInOrganization;
        }

        var now = this.time.GetUtcNow();
        var before = await this.ReadAsync(member, cancellationToken);

        await this.store.RemoveClientAsync(member, client, cancellationToken);
        await this.AnnounceIfSeenDifferentlyAsync(member, before, now, cancellationToken);

        return PresenceWriteOutcome.Recorded;
    }

    /// <summary>Records the presence the caller chose, for a duration or until they change it.</summary>
    /// <param name="state">The state chosen.</param>
    /// <param name="duration">How long it lasts, or <see langword="null" /> for until changed.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The caller's presence after the choice, or <see langword="null" /> where they belong to no organization.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the profile writing grant.</exception>
    /// <remarks>
    /// A choice replaces whatever was chosen before, so a state picked with no duration ends a duration set earlier, and
    /// <see cref="PresenceState.Active" /> with no duration returns the caller to automatic presence. A duration ends in
    /// the caller's own time zone, UTC where their record states none.
    /// </remarks>
    public async Task<OwnPresenceReading?> ChooseAsync(
        PresenceState state,
        PresenceDuration? duration,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.ProfileWrite);

        return await this.ChangeAsync(
            (member, now) => this.store.ChooseAsync(
                member,
                PresenceChoice.Made(state, duration, now, this.timeZones.ZoneOf(member)),
                cancellationToken),
            cancellationToken);
    }

    /// <summary>Records the status message the caller wrote, which the other members of their organization read until it clears.</summary>
    /// <param name="text">What they wrote; nothing but white space clears the message.</param>
    /// <param name="clears">When it clears, or <see langword="null" /> for never.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The caller's presence after the write, or <see langword="null" /> where they belong to no organization.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the text breaks a rule <see cref="StatusMessage.Written" /> states.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the profile writing grant.</exception>
    /// <remarks>A message replaces whatever was written before. It clears in the caller's own time zone, UTC where their record states none.</remarks>
    public async Task<OwnPresenceReading?> WriteStatusMessageAsync(
        string text,
        PresenceDuration? clears,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);

        this.authorization.RequirePermission(MailFathomPermission.ProfileWrite);

        return await this.ChangeAsync(
            (member, now) => this.store.WriteStatusMessageAsync(
                member,
                StatusMessage.Written(text, clears, now, this.timeZones.ZoneOf(member)),
                cancellationToken),
            cancellationToken);
    }

    /// <summary>Records where the caller works from, for today alone or until they change it.</summary>
    /// <param name="mode">Office or remote.</param>
    /// <param name="todayOnly"><see langword="true" /> for today alone, which ends at the caller's own next midnight.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The caller's presence after the write, or <see langword="null" /> where they belong to no organization.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the profile writing grant.</exception>
    public async Task<OwnPresenceReading?> ChooseWorkModeAsync(
        WorkMode mode,
        bool todayOnly,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.ProfileWrite);

        return await this.ChangeAsync(
            (member, now) => this.store.ChooseWorkModeAsync(
                member,
                WorkModeChoice.Made(mode, todayOnly, now, this.timeZones.ZoneOf(member)),
                cancellationToken),
            cancellationToken);
    }

    /// <summary>Makes one write to what the caller chose, and tells the caller's own clients where what the others see of them moved.</summary>
    /// <returns>The caller's presence after the write, or <see langword="null" /> where they belong to no organization and nothing was written.</returns>
    private async Task<OwnPresenceReading?> ChangeAsync(
        Func<UserId, DateTimeOffset, Task> write,
        CancellationToken cancellationToken)
    {
        if (await this.ReadMemberAsync(cancellationToken) is not { } member)
        {
            return null;
        }

        var now = this.time.GetUtcNow();
        var before = await this.ReadAsync(member, cancellationToken);

        await write(member, now);
        var after = await this.AnnounceIfSeenDifferentlyAsync(member, before, now, cancellationToken);

        return OwnPresenceReading.Of(after, now);
    }

    /// <summary>Re-reads the member after a write and tells the member's own clients when what the others see of them moved.</summary>
    /// <returns>The member's presence after the write.</returns>
    private async Task<MemberPresence> AnnounceIfSeenDifferentlyAsync(
        UserId member,
        MemberPresence before,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var after = await this.ReadAsync(member, cancellationToken);

        if (before.SeenByOthersAt(now) != after.SeenByOthersAt(now))
        {
            this.signals.Publish(ClientSignal.PresenceChanged(member));
        }

        return after;
    }

    private async Task SweepIfDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (this.sweeps.TryClaimAt(now))
        {
            await this.store.RemoveExpiredClientsAsync(now, MostSweptClients, cancellationToken);
        }
    }

    private async Task<MemberPresence> ReadAsync(UserId member, CancellationToken cancellationToken) =>
        (await this.store.ReadAsync([member], cancellationToken)).GetValueOrDefault(member, MemberPresence.Unknown);

    /// <summary>Reads who the caller is, or nothing where they belong to no organization and so have no presence.</summary>
    private async Task<UserId?> ReadMemberAsync(CancellationToken cancellationToken)
    {
        var scope = await this.ownership.ReadScopeAsync(cancellationToken);

        return scope.OrganizationBook?.Organization is null ? null : scope.User;
    }
}
