// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.AI.Decisions;

/// <summary>One answer a question offers: the value an answer names it by, and what choosing it means.</summary>
/// <param name="Value">The value an answer names this option by, unique within its question.</param>
/// <param name="Description">What the model is told choosing this option means; empty where the value says it alone.</param>
public sealed record DecisionQuestionOption(string Value, string Description);
