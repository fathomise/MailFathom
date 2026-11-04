// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Mutations.Authoring.Idempotency;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Keeps the keys of people's mailbox acts the way the persisted store does, so two instances can stand for two replicas.</summary>
/// <remarks>
/// <para>
/// Two instances built with <see cref="AnotherReplica" /> share their rows, which is what a second replica is: its own
/// object over the one table. An insert that finds the row already there leaves it alone, as the unique index refuses a
/// second one; the commit that carried it is then the caller's to report as a conflict.
/// </para>
/// <para>
/// What it does not model is the isolation between sessions: a row is visible to every reader as soon as it is staged.
/// <see cref="CompetingWriter" /> is how a test states the one interleaving that matters instead.
/// </para>
/// </remarks>
internal sealed class InMemoryMailActKeyStore : IMailActKeyStore
{
    private readonly Dictionary<(UserId User, string Key, StoredEmailId StoredEmail), Row> rows;

    /// <summary>Initializes a store over a table of its own.</summary>
    public InMemoryMailActKeyStore()
        : this([])
    {
    }

    private InMemoryMailActKeyStore(Dictionary<(UserId User, string Key, StoredEmailId StoredEmail), Row> rows) =>
        this.rows = rows;

    /// <summary>Gets how many rows the table holds.</summary>
    internal int Count => this.rows.Count;

    /// <summary>Gets or sets what another replica commits right after this one's next read inside a session found nothing.</summary>
    internal Action? CompetingWriter { get; set; }

    /// <summary>Builds the store a second replica reads and writes the same table through.</summary>
    /// <returns>A store over the same rows.</returns>
    internal InMemoryMailActKeyStore AnotherReplica() => new(this.rows);

    /// <summary>Writes a row as a committed first attempt left it.</summary>
    /// <param name="user">The person who made the act.</param>
    /// <param name="key">The key, with the digest of the request it was first used with.</param>
    /// <param name="storedEmail">The message.</param>
    /// <param name="outcome">What the first attempt answered.</param>
    /// <param name="recordedAt">When it committed.</param>
    internal void Seed(
        UserId user,
        MailActIdempotencyKey key,
        StoredEmailId storedEmail,
        MailActOutcome outcome,
        DateTimeOffset recordedAt) =>
        this.rows.TryAdd((user, key.Value, storedEmail), new Row(new MailActKeyEntry(key.Fingerprint, outcome), recordedAt));

    /// <inheritdoc />
    public Task<MailActKeyEntry?> FindAsync(
        UserId user,
        string key,
        StoredEmailId storedEmail,
        CancellationToken cancellationToken) =>
        Task.FromResult(this.rows.TryGetValue((user, key, storedEmail), out var row) ? row.Entry : null);

    /// <inheritdoc />
    public Task<MailActKeyEntry?> FindAsync(
        IPersistenceSession session,
        UserId user,
        string key,
        StoredEmailId storedEmail,
        CancellationToken cancellationToken)
    {
        var found = this.rows.TryGetValue((user, key, storedEmail), out var row) ? row.Entry : null;

        if (this.CompetingWriter is { } competing)
        {
            this.CompetingWriter = null;
            competing();
        }

        return Task.FromResult(found);
    }

    /// <inheritdoc />
    public Task<string?> FindFingerprintAsync(UserId user, string key, CancellationToken cancellationToken) =>
        Task.FromResult(
            this.rows
                .Where(row => row.Key.User == user && row.Key.Key == key)
                .Select(static row => row.Value.Entry.Fingerprint)
                .FirstOrDefault());

    /// <inheritdoc />
    public Task StageAsync(
        IPersistenceSession session,
        UserId user,
        MailActIdempotencyKey key,
        StoredEmailId storedEmail,
        MailActOutcome outcome,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken)
    {
        this.Seed(user, key, storedEmail, outcome, recordedAt);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> EraseRecordedBeforeAsync(DateTimeOffset recordedBefore, int limit, CancellationToken cancellationToken)
    {
        var expiring = this.rows
            .Where(row => row.Value.RecordedAt < recordedBefore)
            .OrderBy(static row => row.Value.RecordedAt)
            .Take(limit)
            .Select(static row => row.Key)
            .ToArray();

        foreach (var key in expiring)
        {
            this.rows.Remove(key);
        }

        return Task.FromResult(expiring.Length);
    }

    private sealed record Row(MailActKeyEntry Entry, DateTimeOffset RecordedAt);
}
