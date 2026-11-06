// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Rules.Editing;

/// <summary>What an import does to one rule, and whether the rule runs afterwards.</summary>
/// <param name="Effect">What the import does to it.</param>
/// <param name="Rule">
/// The rule's identifier. An added rule's is the one the import planned it under, which only a committed import keeps: a
/// preview mints one it never stores.
/// </param>
/// <param name="Name">The rule's name.</param>
/// <param name="Enabled">Whether a pass applies the rule afterwards, or applied it before it was removed.</param>
public sealed record MailRuleImportDifference(MailRuleImportEffect Effect, MailRuleId Rule, string Name, bool Enabled);
