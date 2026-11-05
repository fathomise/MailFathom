// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Rules.Sets;

/// <summary>Remembers each account's compiled rule set by its version, so a pass compiles a set only after it changed.</summary>
/// <remarks>
/// <para>
/// One process's, and correct on every replica for that reason rather than despite it: an entry is keyed by the set's
/// version and the bounds it was compiled under rather than by "current", so a commit on another replica produces a
/// version this cache has never seen, the next pass misses, and the superseded entry is simply replaced. Nothing is ever
/// invalidated.
/// </para>
/// <para>
/// The version rather than the revision, because the revision leaves a rule's name out and the compiled set carries it:
/// a write that only renames a rule keeps the revision, and keying on it would go on reporting the old name in every
/// execution the set records until something else changed.
/// </para>
/// <para>
/// It holds one entry per account, which is what bounds it: a version an account has left is never asked for again, so
/// keeping more than the newest would only hold compiled conditions nobody runs. The account count is bounded as well,
/// and past it the cache starts over rather than tracking which account was read least recently.
/// </para>
/// </remarks>
public sealed class MailRuleSetCompilationCache
{
    /// <summary>The most accounts whose compiled sets one process keeps.</summary>
    public const int MaximumCachedAccounts = 4_096;

    private readonly Lock entriesLock = new();
    private readonly Dictionary<MailAccountId, (long Version, MailRuleConditionBounds Bounds, MailRuleSet Set)> entries = [];

    /// <summary>Finds the compiled set of one account at one version, compiled under the given bounds.</summary>
    /// <param name="account">The account.</param>
    /// <param name="version">The version the stored set has now.</param>
    /// <param name="bounds">The bounds in force now.</param>
    /// <param name="ruleSet">The compiled set, when one is remembered.</param>
    /// <returns><see langword="true" /> when the set is remembered.</returns>
    public bool TryFind(
        MailAccountId account,
        long version,
        MailRuleConditionBounds bounds,
        [NotNullWhen(true)] out MailRuleSet? ruleSet)
    {
        lock (this.entriesLock)
        {
            if (this.entries.TryGetValue(account, out var entry) && entry.Version == version && entry.Bounds == bounds)
            {
                ruleSet = entry.Set;

                return true;
            }
        }

        ruleSet = null;

        return false;
    }

    /// <summary>Remembers the compiled set of one account, replacing whatever version it remembered before.</summary>
    /// <param name="account">The account.</param>
    /// <param name="version">The version of the stored set it was compiled from.</param>
    /// <param name="bounds">The bounds the set was compiled under.</param>
    /// <param name="ruleSet">The compiled set.</param>
    public void Remember(
        MailAccountId account,
        long version,
        MailRuleConditionBounds bounds,
        MailRuleSet ruleSet)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(ruleSet);

        lock (this.entriesLock)
        {
            if (this.entries.Count >= MaximumCachedAccounts && !this.entries.ContainsKey(account))
            {
                this.entries.Clear();
            }

            this.entries[account] = (version, bounds, ruleSet);
        }
    }
}
