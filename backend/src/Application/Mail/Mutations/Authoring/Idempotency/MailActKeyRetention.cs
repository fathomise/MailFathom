// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Mail.Mutations.Authoring.Idempotency;

/// <summary>Ages out the keys people's mailbox acts were made under.</summary>
/// <remarks>
/// A key is kept for a day, which is what a transport retry or a client coming back from a lost connection needs and
/// no longer: the key and its digest say what somebody asked of their mailbox, so keeping them is bounded the way the
/// rest of that history is. Entries are per person rather than per account, so one pass covers every person and is safe
/// on every replica — a delete that finds the row already gone has nothing left to do.
/// </remarks>
public sealed class MailActKeyRetention
{
    /// <summary>The time a key is kept after its act was committed.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    /// <summary>The most entries one pass deletes, so one pass is a bounded piece of work however many a busy day left.</summary>
    public const int MaximumEntriesErasedPerPass = 5_000;

    private readonly IMailActKeyStore store;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the sweep over the store it deletes from.</summary>
    /// <param name="store">Holds the keys.</param>
    /// <param name="timeProvider">Reads the current time.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public MailActKeyRetention(IMailActKeyStore store, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.store = store;
        this.timeProvider = timeProvider;
    }

    /// <summary>Deletes the keys whose window has ended.</summary>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>How many entries were deleted, at most <see cref="MaximumEntriesErasedPerPass" />.</returns>
    public Task<int> EraseExpiredAsync(CancellationToken cancellationToken) =>
        this.store.EraseRecordedBeforeAsync(
            this.timeProvider.GetUtcNow() - Window,
            MaximumEntriesErasedPerPass,
            cancellationToken);
}
