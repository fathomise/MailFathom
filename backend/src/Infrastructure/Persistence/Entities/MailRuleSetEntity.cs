// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One mail account's rule set: the row a write locks and the version every committed write moves by one.</summary>
/// <remarks>
/// An account nobody has written a rule for has no row and reads as version <c>0</c>; the first write creates the row in
/// its own transaction before it locks it, so a first write is serialized exactly like any later one.
/// </remarks>
internal sealed class MailRuleSetEntity
{
    /// <summary>Gets or sets the account the rules belong to, which is the row's whole identity.</summary>
    public required string MailboxAccountId { get; set; }

    /// <summary>Gets or sets how many writes have committed to the set.</summary>
    public long Version { get; set; }
}
