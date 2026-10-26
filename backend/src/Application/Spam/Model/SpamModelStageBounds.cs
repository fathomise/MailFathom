// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Spam.Model;

/// <summary>What the deployment allows the model stage to spend: how many calls run at once, and how much of a body one carries.</summary>
/// <remarks>
/// The deployment's rather than an account's, because both bound a resource the process bears — the provider's
/// concurrency and what leaves per message — rather than what anybody's verdict says. The concurrency bound is one
/// replica's, so a deployment of several replicas runs that many calls on each.
/// </remarks>
/// <param name="MaximumConcurrentCalls">How many model calls one replica runs at once.</param>
/// <param name="MaximumBodyCharacters">The most characters of a message's plain-text body one call carries.</param>
public sealed record SpamModelStageBounds(int MaximumConcurrentCalls, int MaximumBodyCharacters)
{
    /// <summary>The calls one replica runs at once when an operator names no bound.</summary>
    public const int DefaultMaximumConcurrentCalls = 4;

    /// <summary>The body characters one call carries when an operator names no bound.</summary>
    /// <remarks>
    /// About two thousand tokens of English and somewhat more of Polish, which holds the whole of almost every message a
    /// person decides about from what they read first, and is the figure ADR 0041 prices a message at.
    /// </remarks>
    public const int DefaultMaximumBodyCharacters = 8_000;

    /// <summary>Gets the bounds a deployment that names none runs with.</summary>
    public static SpamModelStageBounds Default { get; } = new(DefaultMaximumConcurrentCalls, DefaultMaximumBodyCharacters);
}
