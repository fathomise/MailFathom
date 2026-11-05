// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Rules.Actions;

/// <summary>One action a pass will ask the mailbox for, and the rule that asked.</summary>
/// <param name="RuleId">The identifier of the rule the action was declared on, which is what the request is identified by.</param>
/// <param name="RuleName">The name the rule carried when it was evaluated, which the history reads the decision by.</param>
/// <param name="Action">The change asked for.</param>
/// <param name="Position">Where the action sits in the order its own rule declares its changes, counted from zero.</param>
/// <remarks>
/// The rule's identifier travels with the action because it is half of the request's idempotency identity: the same rule
/// asking again for the same email is the same request, and a different rule asking for the same change is a different
/// one. The identifier rather than the name, so a renamed rule is still the same requester and does not ask again, and so
/// nothing a person typed reaches the mutation trail.
/// <para>
/// The position is the rule's own, not the plan's. A plan reorders across rules so that a flag is written before the
/// occurrence carrying it is moved, so the plan's order says when a change is applied while this says which of the
/// rule's declared changes it is — which is what a record of the decision has to name it by.
/// </para>
/// </remarks>
public sealed record PlannedMailRuleAction(MailRuleId RuleId, string RuleName, MailRuleAction Action, int Position);
