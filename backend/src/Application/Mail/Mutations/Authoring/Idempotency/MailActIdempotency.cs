// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Mail.Mutations.Authoring.Failures;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Mail.Mutations.Authoring.Idempotency;

/// <summary>Answers whether a person's key was already used for a message, and keeps it once its change commits.</summary>
/// <remarks>
/// <para>
/// The person is the signed-in user the use case was reached as, so one person's key never answers another's request
/// even when both clients generated the same text.
/// </para>
/// <para>
/// The recorders ask twice. Before they look the message up they ask without a session, because a repeat after the
/// message was deleted must still be answered and finds no message to resolve. Inside the commit they ask again in the
/// session that will write, because a repeat reaching another replica while the first attempt is still running passed
/// the first question before anything was committed: the second asks from a fresh read and the unique index closes
/// what remains.
/// </para>
/// </remarks>
public sealed class MailActIdempotency
{
    private readonly AccessAuthorization authorization;
    private readonly IMailActKeyStore store;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the service over the store the keys are kept in.</summary>
    /// <param name="authorization">Answers which person reached this use case.</param>
    /// <param name="store">Holds the keys.</param>
    /// <param name="timeProvider">Reads the time an act is committed at.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public MailActIdempotency(AccessAuthorization authorization, IMailActKeyStore store, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.authorization = authorization;
        this.store = store;
        this.timeProvider = timeProvider;
    }

    /// <summary>Refuses a key the person already used for a different act, before any message of the request is touched.</summary>
    /// <param name="key">The key the request was made under.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A task that completes when the key is new or was first used for this same act.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller is not a person.</exception>
    /// <exception cref="MailFlagChangeInvalidException">Thrown when the key names a different act than the one it was first used for.</exception>
    public async Task RequireSameActAsync(MailActIdempotencyKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);

        var first = await this.store.FindFingerprintAsync(this.authorization.RequireUser(), key.Value, cancellationToken);

        if (first is not null && !StringComparer.Ordinal.Equals(first, key.Fingerprint))
        {
            throw MailFlagChangeInvalidException.RequestIdReusedForAnotherAct();
        }
    }

    /// <summary>Reads what the first attempt answered for a message.</summary>
    /// <param name="key">The key the request was made under.</param>
    /// <param name="storedEmail">The message.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The first outcome, or <see langword="null" /> when the key has not been used on the message.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key" /> is <see langword="null" />.</exception>
    /// <exception cref="MailFlagChangeInvalidException">Thrown when the entry was made for a different act.</exception>
    public async Task<MailActOutcome?> FindAsync(
        MailActIdempotencyKey key,
        StoredEmailId storedEmail,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);

        return SameActOrNull(
            await this.store.FindAsync(this.authorization.RequireUser(), key.Value, storedEmail, cancellationToken),
            key);
    }

    /// <summary>Reads what the first attempt answered for a message, from the session that will commit the change.</summary>
    /// <param name="session">The session the change is staged in.</param>
    /// <param name="key">The key the request was made under.</param>
    /// <param name="storedEmail">The message.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The first outcome, or <see langword="null" /> when the key has not been used on the message.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="key" /> is <see langword="null" />.</exception>
    /// <exception cref="MailFlagChangeInvalidException">Thrown when the entry was made for a different act.</exception>
    public async Task<MailActOutcome?> FindAsync(
        IPersistenceSession session,
        MailActIdempotencyKey key,
        StoredEmailId storedEmail,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(key);

        return SameActOrNull(
            await this.store.FindAsync(session, this.authorization.RequireUser(), key.Value, storedEmail, cancellationToken),
            key);
    }

    /// <summary>Stages the key of a message whose change the session commits.</summary>
    /// <param name="session">The session the change is staged in.</param>
    /// <param name="key">The key the request was made under.</param>
    /// <param name="storedEmail">The message.</param>
    /// <param name="outcome">What the act answers for the message.</param>
    /// <param name="cancellationToken">Cancels the staging.</param>
    /// <returns>A task that completes once the key is staged.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public Task StageAsync(
        IPersistenceSession session,
        MailActIdempotencyKey key,
        StoredEmailId storedEmail,
        MailActOutcome outcome,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(outcome);

        return this.store.StageAsync(
            session,
            this.authorization.RequireUser(),
            key,
            storedEmail,
            outcome,
            this.timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private static MailActOutcome? SameActOrNull(MailActKeyEntry? entry, MailActIdempotencyKey key)
    {
        if (entry is null)
        {
            return null;
        }

        return StringComparer.Ordinal.Equals(entry.Fingerprint, key.Fingerprint)
            ? entry.Outcome
            : throw MailFlagChangeInvalidException.RequestIdReusedForAnotherAct();
    }
}
