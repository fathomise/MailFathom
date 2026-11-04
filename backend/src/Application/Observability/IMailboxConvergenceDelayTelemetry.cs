// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Observability;

/// <summary>Measures how long a change waited between being written down and reaching the ending it settled in.</summary>
/// <remarks>
/// <para>
/// That wait is what a person's change costs every other client of the same mailbox, and it is bounded by the longest
/// stage of the synchronization run the converger rides on rather than by anything a setting names. Whether the
/// converger leaves that run is decided from this measurement, so it is taken once per record that ended rather than
/// per attempt: an attempt that leaves the record outstanding is part of the wait, not an ending of it.
/// </para>
/// <para>
/// The record's origin and the lifecycle it ended in are the only dimensions. No account, occurrence, requester
/// identity, or anything read out of a message reaches the measurement, and it is one replica's: the replica that wrote
/// the ending records it.
/// </para>
/// </remarks>
public interface IMailboxConvergenceDelayTelemetry
{
    /// <summary>Records the wait of one record that has just reached a terminal stage carried to a mail server.</summary>
    /// <param name="record">The record as it stands once its ending is durable.</param>
    void Ended(MailboxMutationRecord record);
}
