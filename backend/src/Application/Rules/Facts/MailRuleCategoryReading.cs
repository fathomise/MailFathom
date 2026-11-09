// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Collections.Frozen;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Rules.Facts;

/// <summary>What one batch of a rule pass knows about its mail's categories.</summary>
/// <param name="Shown">The categories each message holds and its account shows; a message holding none is absent.</param>
/// <param name="Awaited">The messages a rule naming the fact has to wait for, because a category may still arrive.</param>
public sealed record MailRuleCategoryReading(
    IReadOnlyDictionary<StoredEmailId, IReadOnlyList<string>> Shown,
    IReadOnlySet<StoredEmailId> Awaited)
{
    /// <summary>Gets the reading of a batch no rule asks about, which costs that batch no read.</summary>
    public static MailRuleCategoryReading None { get; } = new(
        FrozenDictionary<StoredEmailId, IReadOnlyList<string>>.Empty,
        FrozenSet<StoredEmailId>.Empty);

    /// <summary>Reads the categories one message holds.</summary>
    /// <param name="email">The message.</param>
    /// <returns>Its categories, empty where it holds none.</returns>
    public IReadOnlyList<string> ShownOn(StoredEmailId email) => this.Shown.GetValueOrDefault(email, []);
}
