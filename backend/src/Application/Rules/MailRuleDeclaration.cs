// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Jobs.Scheduling;
using MailFathom.Application.Rules.Actions;

namespace MailFathom.Application.Rules;

/// <summary>One rule of a mail account exactly as it is stored: what a person wrote, before its condition is compiled.</summary>
/// <param name="Id">The identifier the rule was minted with when it was created.</param>
/// <param name="Name">The name a person reads the rule by, unique within its account without regard to case.</param>
/// <param name="Enabled">Whether the rule takes part in a pass; a created rule starts disabled.</param>
/// <param name="ConditionText">The condition as its author wrote it.</param>
/// <param name="Actions">What a match does to the matching email, in the order the changes are applied.</param>
/// <param name="StopWhenMatched">Whether a match ends the pass rather than continuing to the rules below.</param>
/// <param name="Triggers">The automatic triggers the rule takes part in, empty for a rule only a requested walk runs.</param>
/// <param name="Schedule">The occasions a scheduled walk happens on, and <see langword="null" /> for a rule declaring no schedule.</param>
/// <remarks>
/// <para>
/// Deliberately separate from <see cref="MailRule" />, which carries a compiled condition and no authored text. A
/// declaration is what a write validates, what the archive keeps, and what a revision is derived from; a pass compiles it
/// and holds only the rule. A condition can legitimately contain an address its author typed, so keeping the text out of
/// the rule that is passed around is what stops a run record that names a rule from carrying somebody's address with it.
/// </para>
/// <para>
/// It names no account, because the account a rule is stored under is its whole scope.
/// </para>
/// </remarks>
public sealed record MailRuleDeclaration(
    MailRuleId Id,
    string Name,
    bool Enabled,
    string ConditionText,
    IReadOnlyList<MailRuleAction> Actions,
    bool StopWhenMatched,
    IReadOnlyList<MailRuleTrigger> Triggers,
    JobRecurrence? Schedule);
