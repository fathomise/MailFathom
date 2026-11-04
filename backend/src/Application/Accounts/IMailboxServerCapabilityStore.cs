// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Accounts;

/// <summary>Holds the capabilities each account's IMAP server last advertised after authentication.</summary>
/// <remarks>
/// <para>
/// A custodian deciding at the moment of a person's act has no IMAP session open and must not open one, so it reads the
/// last observation here. The custody resolution path and the write session are the only consumers; every
/// authenticated session against the source is the only writer. See
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0039-one-folder-model-and-one-implementation-per-custody-phase.md#where-the-capabilities-come-from">ADR 0039</see>.
/// </para>
/// <para>
/// It takes no persistence session and joins no transaction. A recording happens while a session is being established,
/// which no unit of work over the mailbox may span, and a read answers a fact about the server rather than a value a
/// caller's transaction could change.
/// </para>
/// </remarks>
public interface IMailboxServerCapabilityStore
{
    /// <summary>Reads what the account's server last advertised.</summary>
    /// <param name="account">The account whose server is asked about.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The last observation, or <see cref="MailboxServerCapabilityObservation.NeverObserved" /> where no session has reached the server.</returns>
    Task<MailboxServerCapabilityObservation> ReadAsync(MailAccountId account, CancellationToken cancellationToken);

    /// <summary>Records what an authenticated session against the account's server has just observed it advertise.</summary>
    /// <param name="account">The account the session belongs to.</param>
    /// <param name="advertised">What the server advertised after authentication.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes once the observation is durable or was found older than the one already stored.</returns>
    /// <remarks>
    /// The observation is stamped when it is recorded, and two replicas recording at once resolve by that instant: the
    /// later one stands, and an earlier one arriving after it changes nothing. An account whose row does not exist yet
    /// keeps no observation, because the row is created by the first run that binds one of its folders; the next session
    /// after that records one.
    /// </remarks>
    Task RecordAsync(
        MailAccountId account,
        MailboxServerCapabilities advertised,
        CancellationToken cancellationToken);
}
