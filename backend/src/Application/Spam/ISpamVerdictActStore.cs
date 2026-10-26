// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Spam;

namespace MailFathom.Application.Spam;

/// <summary>Keeps the record of what this deployment's spam verdicts did to each message, in every custody phase alike.</summary>
/// <remarks>
/// <para>
/// A mirrored account's acts also leave a mutation record, and a held account's leave none, so a rule read from the
/// mutation records alone would hold on one kind of account and lapse on the other. This record is written beside the
/// act on every account, in the act's own transaction, which is what lets the classifier refuse its own act as evidence
/// and the recorder refuse to repeat an act somebody undid, with one answer whatever the account's phase.
/// </para>
/// <para>
/// It is keyed by the stored email, so it survives the message moving and is erased with it.
/// </para>
/// </remarks>
public interface ISpamVerdictActStore
{
    /// <summary>Reads every act a verdict of this deployment has taken on one message.</summary>
    /// <param name="emailId">The stored email.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The acts, empty where none was taken.</returns>
    Task<IReadOnlySet<SpamVerdictAct>> FindAsync(StoredEmailId emailId, CancellationToken cancellationToken);

    /// <summary>Records one act inside the transaction that took it.</summary>
    /// <param name="session">The transaction the act commits in.</param>
    /// <param name="emailId">The stored email the act was taken on.</param>
    /// <param name="act">What was done.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the record is staged.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <remarks>Recording an act already recorded changes nothing, so a retried commit records it once.</remarks>
    Task RecordAsync(
        IPersistenceSession session,
        StoredEmailId emailId,
        SpamVerdictAct act,
        CancellationToken cancellationToken);
}
