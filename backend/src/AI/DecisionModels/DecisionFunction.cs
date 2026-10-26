// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.AI.DecisionModels;

/// <summary>Names each piece of work that asks a decision model a closed question, and so may be routed to a model of its own.</summary>
/// <remarks>
/// What a capability is to the chat declaration, a function is to the decision one: the member name is the key an
/// operator routes it under, <c>Decisions:&lt;Function&gt;:Model</c>, and a function naming no model of its own runs on
/// <c>Decisions:DefaultModel</c>.
/// </remarks>
public enum DecisionFunction
{
    /// <summary>Deciding whether a message is spam, and which kind.</summary>
    SpamClassification = 0,
}
