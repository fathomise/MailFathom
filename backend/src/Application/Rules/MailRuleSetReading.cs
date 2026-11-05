// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Rules.Sets;

namespace MailFathom.Application.Rules;

/// <summary>One account's stored rules, and the enabled rules that hold the set because they no longer compile.</summary>
/// <param name="Stored">The rules as they are stored.</param>
/// <param name="UncompiledRules">The enabled rules whose condition does not compile under the bounds in force, empty when the set runs.</param>
public sealed record MailRuleSetReading(StoredMailRuleSet Stored, IReadOnlyList<MailRuleId> UncompiledRules)
{
    /// <summary>Gets whether a pass runs none of the account's rules until a write repairs the ones that stopped compiling.</summary>
    public bool IsHeld => this.UncompiledRules.Count > 0;
}
