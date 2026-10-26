// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Spam;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Spam;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Holds one classification per occurrence in memory, the way the real upsert behaves.</summary>
/// <remarks>
/// The writes are kept in order beside the current state, because what a test about idempotency has to establish is how
/// many times the store was written to rather than only what it ends up holding: a second classification that replaced
/// the first with an identical record leaves exactly the state a skipped write does.
/// </remarks>
internal sealed class InMemoryEmailSpamClassificationStore : IEmailSpamClassificationStore
{
    private readonly Dictionary<StoredEmailId, SpamClassification> classificationsByEmail = [];
    private readonly List<SpamClassification> saved = [];
    private SpamClassification? heldAfterTheNextRead;

    /// <summary>Gets every classification staged through the port, oldest first.</summary>
    internal IReadOnlyList<SpamClassification> Saved => this.saved;

    /// <summary>Puts a classification into the state an earlier evaluation would have left.</summary>
    internal void Hold(SpamClassification classification) =>
        this.classificationsByEmail[classification.EmailId] = classification;

    /// <summary>Records a classification as another writer would, just after the next read has answered without it.</summary>
    /// <remarks>Models a write committed between a caller's read and its own write, which is the race the conditional write exists for.</remarks>
    internal void HoldAfterTheNextRead(SpamClassification classification) => this.heldAfterTheNextRead = classification;

    /// <inheritdoc />
    public Task<SpamClassification?> FindAsync(StoredEmailId emailId, CancellationToken cancellationToken)
    {
        var found = this.classificationsByEmail.GetValueOrDefault(emailId);

        if (this.heldAfterTheNextRead is { } arriving)
        {
            this.Hold(arriving);
            this.heldAfterTheNextRead = null;
        }

        return Task.FromResult(found);
    }

    /// <inheritdoc />
    public Task<bool> SaveAsync(
        IPersistenceSession session,
        SpamClassification classification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(classification);

        if (this.classificationsByEmail.TryGetValue(classification.EmailId, out var recorded)
            && !classification.MayReplace(recorded.DecidedBy))
        {
            return Task.FromResult(false);
        }

        this.classificationsByEmail[classification.EmailId] = classification;
        this.saved.Add(classification);

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> LiftToSpamAsync(
        IPersistenceSession session,
        SpamClassification classification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(classification);

        if (this.classificationsByEmail.TryGetValue(classification.EmailId, out var recorded)
            && recorded.Verdict is SpamVerdict.Spam)
        {
            return Task.FromResult(false);
        }

        return this.SaveAsync(session, classification, cancellationToken);
    }
}
