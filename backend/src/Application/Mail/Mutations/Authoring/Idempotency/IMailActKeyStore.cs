// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Mail.Mutations.Authoring.Idempotency;

/// <summary>Keeps the keys people's mailbox acts were made under, and what each one answered the first time.</summary>
/// <remarks>
/// <para>
/// One entry per person, key, and stored email, written in the transaction that commits that message's change and
/// never anywhere a replica's memory is the only copy: a repeat reaches whichever replica the balancer picks.
/// </para>
/// <para>
/// An entry is not a column of the mutation record, because a record ends with its stored email and a held account's
/// local commit writes none, while the key has to outlive both for as long as <see cref="MailActKeyRetention.Window" />.
/// </para>
/// </remarks>
public interface IMailActKeyStore
{
    /// <summary>Reads the entry a person's key holds for one message, as the store has it committed.</summary>
    /// <param name="user">The person who made the act.</param>
    /// <param name="key">The text the client generated for the act.</param>
    /// <param name="storedEmail">The message.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The entry, or <see langword="null" /> when the key has not been used on that message.</returns>
    Task<MailActKeyEntry?> FindAsync(UserId user, string key, StoredEmailId storedEmail, CancellationToken cancellationToken);

    /// <summary>Reads the entry a person's key holds for one message, inside the session that will commit the change.</summary>
    /// <param name="session">The session the change is staged in.</param>
    /// <param name="user">The person who made the act.</param>
    /// <param name="key">The text the client generated for the act.</param>
    /// <param name="storedEmail">The message.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The entry, or <see langword="null" /> when the key has not been used on that message.</returns>
    Task<MailActKeyEntry?> FindAsync(
        IPersistenceSession session,
        UserId user,
        string key,
        StoredEmailId storedEmail,
        CancellationToken cancellationToken);

    /// <summary>Reads the digest a person's key was first used with, whichever message it was used on.</summary>
    /// <param name="user">The person who made the act.</param>
    /// <param name="key">The text the client generated for the act.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The digest, or <see langword="null" /> when the key has not been used.</returns>
    Task<string?> FindFingerprintAsync(UserId user, string key, CancellationToken cancellationToken);

    /// <summary>Stages the entry for a message whose change the session commits.</summary>
    /// <param name="session">The session the change is staged in.</param>
    /// <param name="user">The person who made the act.</param>
    /// <param name="key">The key, with the digest of the whole request.</param>
    /// <param name="storedEmail">The message.</param>
    /// <param name="outcome">What the act answers for the message.</param>
    /// <param name="recordedAt">When the act was committed, which is what the retention counts from.</param>
    /// <param name="cancellationToken">Cancels the staging.</param>
    /// <returns>A task that completes once the entry is staged.</returns>
    /// <remarks>A second writer's insert loses on the unique index, which the session reports as a concurrency conflict so the commit is retried from a fresh read and finds this entry.</remarks>
    Task StageAsync(
        IPersistenceSession session,
        UserId user,
        MailActIdempotencyKey key,
        StoredEmailId storedEmail,
        MailActOutcome outcome,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken);

    /// <summary>Deletes entries recorded before an instant, the oldest first.</summary>
    /// <param name="recordedBefore">The cutoff.</param>
    /// <param name="limit">The most entries one call deletes.</param>
    /// <param name="cancellationToken">Cancels the deletion.</param>
    /// <returns>How many entries were deleted.</returns>
    Task<int> EraseRecordedBeforeAsync(DateTimeOffset recordedBefore, int limit, CancellationToken cancellationToken);
}
