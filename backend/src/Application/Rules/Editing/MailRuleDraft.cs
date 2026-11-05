// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Jobs.Scheduling;
using MailFathom.Application.Rules.Actions;

namespace MailFathom.Application.Rules.Editing;

/// <summary>What a person writes for one rule: everything it declares except its identity, its position, and whether it runs.</summary>
/// <param name="Name">The name a person reads the rule by.</param>
/// <param name="ConditionText">The condition, as written.</param>
/// <param name="Actions">What a match does to the matching email.</param>
/// <param name="StopWhenMatched">Whether a match ends the pass.</param>
/// <param name="Triggers">The automatic triggers the rule takes part in.</param>
/// <param name="Schedule">The occasions a scheduled walk happens on, present exactly when the schedule trigger is.</param>
/// <remarks>
/// The identity is minted when a rule is created, the position is the set's to decide, and whether a rule runs is a
/// write of its own, so a draft carries none of the three: a create always commits the rule disabled, and a replacement
/// keeps all three as they were.
/// </remarks>
public sealed record MailRuleDraft(
    string Name,
    string ConditionText,
    IReadOnlyList<MailRuleAction> Actions,
    bool StopWhenMatched,
    IReadOnlyList<MailRuleTrigger> Triggers,
    JobRecurrence? Schedule)
{
    /// <summary>Declares this draft as the rule with the given identity and state.</summary>
    /// <param name="id">The rule's identifier.</param>
    /// <param name="enabled">Whether the rule runs.</param>
    /// <returns>The declaration.</returns>
    internal MailRuleDeclaration Declare(MailRuleId id, bool enabled) => new(
        id,
        this.Name?.Trim() ?? string.Empty,
        enabled,
        this.ConditionText ?? string.Empty,
        [.. this.Actions ?? []],
        this.StopWhenMatched,
        [.. this.Triggers ?? []],
        this.Schedule);
}
