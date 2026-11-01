// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Presence;

/// <summary>Everything a member's presence is worked out from: their open clients, the state they chose, and when they were last seen.</summary>
/// <param name="Clients">The member's client rows, expired ones included, which every read leaves out by their expiry.</param>
/// <param name="Choice">The state the member chose, or <see langword="null" /> where they chose none.</param>
/// <param name="LastSeen">When the member was last present, or <see langword="null" /> where they never were.</param>
/// <remarks>
/// <para>
/// Presence is never stored: it is worked out from this when it is read, which is what
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0045-a-members-presence-from-the-clients-it-keeps-open-and-the-state-it-chose.md">ADR 0045</see>
/// decides. No open client is <see cref="PresenceState.Offline" />, whatever the member chose; otherwise a choice still
/// in effect is that state; otherwise the most present client answers, <see cref="ClientActivity.Active" /> over
/// <see cref="ClientActivity.Idle" />.
/// </para>
/// <para>
/// A client row counts until its own expiry and not a moment after, so a client that crashed, lost its network, or was
/// last renewed by a replica that has since died stops counting with nothing written for it.
/// </para>
/// </remarks>
public sealed record MemberPresence(
    IReadOnlyList<OpenPresenceClient> Clients,
    PresenceChoice? Choice,
    DateTimeOffset? LastSeen)
{
    /// <summary>How often an open client renews its row.</summary>
    public static readonly TimeSpan RenewalInterval = TimeSpan.FromSeconds(60);

    /// <summary>How long a client row counts after its last renewal.</summary>
    /// <remarks>Three renewal intervals, so one late or lost renewal does not flicker a member to <see cref="PresenceState.Offline" />.</remarks>
    public static readonly TimeSpan ClientLifetime = TimeSpan.FromSeconds(180);

    /// <summary>The most clients one member keeps open at once.</summary>
    public const int MostOpenClients = 16;

    /// <summary>Gets the presence of a member nothing is held about.</summary>
    public static MemberPresence Unknown { get; } = new([], Choice: null, LastSeen: null);

    /// <summary>Gets the member's phone-app sessions, which say whether they use the phone app apart from any state.</summary>
    /// <remarks>Read beside the presence rather than worked into it: a member <see cref="PresenceState.Offline" /> still carries the marker, and only one who chose <see cref="PresenceState.Hidden" /> loses it.</remarks>
    public IReadOnlyList<PhoneAppSession> PhoneAppSessions { get; init; } = [];

    /// <summary>Works out the state the member themselves sees, <see cref="PresenceState.Hidden" /> included.</summary>
    /// <param name="instant">The time of the read.</param>
    /// <returns>The member's own state.</returns>
    public PresenceState OwnStateAt(DateTimeOffset instant)
    {
        var open = this.Clients.Where(client => client.IsOpenAt(instant)).ToArray();

        if (open.Length == 0)
        {
            return PresenceState.Offline;
        }

        if (this.ChoiceInEffectAt(instant) is { } choice)
        {
            return choice.State;
        }

        return open.Any(client => client.Activity == ClientActivity.Active) ? PresenceState.Active : PresenceState.Away;
    }

    /// <summary>Works out what every other member of the organization sees.</summary>
    /// <param name="instant">The time of the read.</param>
    /// <returns>The state, with the last-seen time where the member reads as <see cref="PresenceState.Offline" /> and did not choose to hide, and whether they use the phone app.</returns>
    /// <remarks>
    /// <see cref="PresenceState.Hidden" /> reads as <see cref="PresenceState.Offline" />. A member who chose
    /// <see cref="PresenceState.Hidden" /> shows no last-seen time either, open client or not, because a time would tell
    /// anybody reading it that they were around — and no phone-app marker, because hiding means not being shown.
    /// </remarks>
    public PresenceReading SeenByOthersAt(DateTimeOffset instant)
    {
        var own = this.OwnStateAt(instant);
        var state = own == PresenceState.Hidden ? PresenceState.Offline : own;
        var hidden = this.ChoiceInEffectAt(instant)?.State == PresenceState.Hidden;

        return new PresenceReading(
            state,
            state == PresenceState.Offline && !hidden ? this.LastSeen : null,
            !hidden && this.PhoneAppSessions.Any(session => session.MarksAt(instant)));
    }

    /// <summary>Says whether a renewal at an instant moves the last-seen time forward.</summary>
    /// <param name="instant">The time of the renewal.</param>
    /// <returns><see langword="false" /> while the member's chosen state is <see cref="PresenceState.Hidden" /> or <see cref="PresenceState.Offline" />.</returns>
    /// <remarks>A time that kept moving while a member chose to look absent would tell anybody watching it that they were present after all.</remarks>
    public bool AdvancesLastSeenAt(DateTimeOffset instant) =>
        this.ChoiceInEffectAt(instant)?.State is not (PresenceState.Hidden or PresenceState.Offline);

    /// <summary>Gets the choice, while it has not run out.</summary>
    /// <param name="instant">The time of the read.</param>
    /// <returns>The choice in effect, or <see langword="null" /> where there is none or it has run out.</returns>
    public PresenceChoice? ChoiceInEffectAt(DateTimeOffset instant) =>
        this.Choice is { } choice && choice.IsInEffectAt(instant) ? choice : null;
}
