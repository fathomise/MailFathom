// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Mutations.Authoring.Idempotency;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Mutations;

/// <summary>Keeps the keys people's mailbox acts were made under in PostgreSQL.</summary>
/// <remarks>
/// Every replica reads and writes the same table, so a repeat reaching another replica meets the first attempt's row.
/// A second writer's insert for the same person, key, and message loses on the unique index, which
/// <see cref="PersistenceConcurrencyConflicts" /> recognizes by name so the commit is retried from a fresh read.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailActKeyStore(MailFathomDbContext readContext) : IMailActKeyStore
{
    /// <inheritdoc />
    public async Task<MailActKeyEntry?> FindAsync(
        UserId user,
        string key,
        StoredEmailId storedEmail,
        CancellationToken cancellationToken)
    {
        var userValue = user.Value;
        var emailValue = storedEmail.Value;

        var row = await readContext.MailActKeys
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.UserId == userValue && candidate.Key == key && candidate.StoredEmailId == emailValue,
                cancellationToken);

        return row is null ? null : EntryOf(row);
    }

    /// <inheritdoc />
    public async Task<MailActKeyEntry?> FindAsync(
        IPersistenceSession session,
        UserId user,
        string key,
        StoredEmailId storedEmail,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var userValue = user.Value;
        var emailValue = storedEmail.Value;

        var row = await TrackedEntityLookup.SinglePendingOrPersistedAsync(
            writeContext.MailActKeys,
            writeContext.MailActKeys,
            candidate => candidate.UserId == userValue && candidate.Key == key && candidate.StoredEmailId == emailValue,
            cancellationToken);

        return row is null ? null : EntryOf(row);
    }

    /// <inheritdoc />
    public Task<string?> FindFingerprintAsync(UserId user, string key, CancellationToken cancellationToken)
    {
        var userValue = user.Value;

        return readContext.MailActKeys
            .AsNoTracking()
            .Where(candidate => candidate.UserId == userValue && candidate.Key == key)
            .Select(candidate => candidate.Fingerprint)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task StageAsync(
        IPersistenceSession session,
        UserId user,
        MailActIdempotencyKey key,
        StoredEmailId storedEmail,
        MailActOutcome outcome,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(outcome);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        writeContext.MailActKeys.Add(new MailActKeyEntity
        {
            Id = Guid.CreateVersion7(recordedAt),
            UserId = user.Value,
            Key = key.Value,
            StoredEmailId = storedEmail.Value,
            Fingerprint = key.Fingerprint,
            Outcome = MailActKeyMapping.ToDocument(outcome),
            RecordedAt = recordedAt,
        });
    }

    /// <inheritdoc />
    /// <remarks>
    /// The identities are read first and deleted by identity for the reason the notification and audit sweeps give: a
    /// delete that is both ordered and limited either fails to translate or becomes a subquery whose shape depends on
    /// the provider. A replica that finds a row already gone deleted nothing, so the sweep needs no lease.
    /// </remarks>
    public async Task<int> EraseRecordedBeforeAsync(
        DateTimeOffset recordedBefore,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var expiringIds = await readContext.MailActKeys
            .AsNoTracking()
            .Where(row => row.RecordedAt < recordedBefore)
            .OrderBy(row => row.RecordedAt)
            .ThenBy(row => row.Id)
            .Take(limit)
            .Select(row => row.Id)
            .ToArrayAsync(cancellationToken);

        if (expiringIds.Length == 0)
        {
            return 0;
        }

        return await readContext.MailActKeys
            .Where(row => expiringIds.Contains(row.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static MailActKeyEntry EntryOf(MailActKeyEntity row) =>
        new(row.Fingerprint, MailActKeyMapping.ToOutcome(row.Outcome));
}
