// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Accounts;

/// <summary>What an account's IMAP server last advertised after authentication, and when that was observed.</summary>
/// <remarks>
/// <para>
/// Every authenticated session against the source refreshes it, so a server upgraded, migrated, or reconfigured under
/// an account that stays is picked up by the next session rather than by somebody adding the account again. It is the
/// advertisement after login because servers list far less before authentication. See
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0039-one-folder-model-and-one-implementation-per-custody-phase.md#where-the-capabilities-come-from">ADR 0039</see>.
/// </para>
/// <para>
/// An account whose server was never reached reads as <see cref="NeverObserved" />, which advertises nothing, so an act
/// that depends on a capability is carried server-first until a session has said otherwise.
/// </para>
/// </remarks>
public sealed record MailboxServerCapabilityObservation
{
    private MailboxServerCapabilityObservation(MailboxServerCapabilities advertised, DateTimeOffset? observedAt)
    {
        this.Advertised = advertised;
        this.ObservedAt = observedAt;
    }

    /// <summary>Gets the observation of an account whose server no session has reached yet.</summary>
    public static MailboxServerCapabilityObservation NeverObserved { get; } = new(MailboxServerCapabilities.None, observedAt: null);

    /// <summary>Gets what the server advertised.</summary>
    public MailboxServerCapabilities Advertised { get; }

    /// <summary>Gets when a session observed it, or <see langword="null" /> for an account no session has reached.</summary>
    public DateTimeOffset? ObservedAt { get; }

    /// <summary>Gets whether the server names where it put a message it copied, moved, or appended.</summary>
    /// <remarks>
    /// That is RFC 4315 <c>UIDPLUS</c>, or <c>IMAP4rev2</c>, which RFC 9051 folds it into. It is what lets a move, a
    /// copy, and an append commit locally first and bind to the occurrence the server's <c>COPYUID</c> or
    /// <c>APPENDUID</c> names.
    /// </remarks>
    public bool NamesWhereMessagesLand =>
        (this.Advertised & (MailboxServerCapabilities.UidPlus | MailboxServerCapabilities.Imap4Rev2)) is not MailboxServerCapabilities.None;

    /// <summary>Records what a session observed at one instant.</summary>
    /// <param name="advertised">What the server advertised after authentication.</param>
    /// <param name="observedAt">When the session observed it.</param>
    /// <returns>The observation.</returns>
    public static MailboxServerCapabilityObservation Observed(MailboxServerCapabilities advertised, DateTimeOffset observedAt) =>
        new(advertised, observedAt);
}
