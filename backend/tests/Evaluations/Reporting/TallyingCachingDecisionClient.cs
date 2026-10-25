// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Collections.Concurrent;
using MailFathom.AI.Decisions;
using Microsoft.Extensions.Caching.Distributed;

namespace MailFathom.Evaluations.Reporting;

/// <summary>The run's response cache in front of a decision model, counting every answer as replayed, reused, or asked.</summary>
/// <remarks>The decision counterpart of <see cref="ToolKeyedCachingChatClient" />, tallied the same way so a run's report reads one count for both kinds of call.</remarks>
internal sealed class TallyingCachingDecisionClient(
    IDecisionClient model,
    IDistributedCache cache,
    ConcurrentDictionary<string, byte> writtenKeys,
    CachedAnswerTally tally)
    : DistributedCachingDecisionClient(model, cache)
{
    /// <inheritdoc />
    protected override async Task<DecisionResponse?> ReadCacheAsync(string key, CancellationToken cancellationToken)
    {
        var cached = await base.ReadCacheAsync(key, cancellationToken);

        if (cached is null)
        {
            tally.CountAsked();
        }
        else if (writtenKeys.ContainsKey(key))
        {
            tally.CountReused();
        }
        else
        {
            tally.CountReplayed();
        }

        return cached;
    }

    /// <inheritdoc />
    protected override async Task WriteCacheAsync(string key, DecisionResponse value, CancellationToken cancellationToken)
    {
        await base.WriteCacheAsync(key, value, cancellationToken);

        writtenKeys.TryAdd(key, 0);
    }
}
