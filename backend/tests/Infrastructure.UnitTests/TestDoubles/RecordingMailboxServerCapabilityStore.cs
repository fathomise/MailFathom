// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Collections.Concurrent;
using MailFathom.Application.Accounts;
using MailFathom.Domain.Accounts;

namespace MailFathom.Infrastructure.UnitTests.TestDoubles;

/// <summary>Keeps every recording an IMAP connection made, in order, so a test reads what each establishment observed.</summary>
internal sealed class RecordingMailboxServerCapabilityStore : IMailboxServerCapabilityStore
{
    private readonly ConcurrentQueue<(MailAccountId Account, MailboxServerCapabilities Advertised)> recordings = new();

    internal IReadOnlyList<(MailAccountId Account, MailboxServerCapabilities Advertised)> Recordings => [.. this.recordings];

    /// <summary>Gets the failures the next recordings raise, one each and in order, before recording resumes.</summary>
    internal ConcurrentQueue<Exception> RefusedRecordings { get; } = new();

    public Task<MailboxServerCapabilityObservation> ReadAsync(MailAccountId account, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("An IMAP connection records what a server advertised and never reads it back.");

    public Task RecordAsync(
        MailAccountId account,
        MailboxServerCapabilities advertised,
        CancellationToken cancellationToken)
    {
        if (this.RefusedRecordings.TryDequeue(out var refusal))
        {
            return Task.FromException(refusal);
        }

        this.recordings.Enqueue((account, advertised));

        return Task.CompletedTask;
    }
}
