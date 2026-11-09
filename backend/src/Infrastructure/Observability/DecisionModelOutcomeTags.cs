// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Decisions;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Names how a consultation of a decision model ended, in the form a counter's tag carries it.</summary>
internal static class DecisionModelOutcomeTags
{
    /// <summary>Gets the tag value for one outcome.</summary>
    /// <param name="outcome">The outcome.</param>
    /// <returns>The value the outcome is counted under.</returns>
    internal static string TagOf(DecisionModelOutcome outcome) => outcome switch
    {
        DecisionModelOutcome.Answered => "answered",
        DecisionModelOutcome.NotActivated => "not_activated",
        DecisionModelOutcome.BudgetExhausted => "budget_exhausted",
        DecisionModelOutcome.ProviderUnavailable => "provider_unavailable",
        DecisionModelOutcome.AnswerUnreadable => "answer_unreadable",
        DecisionModelOutcome.InputTooLarge => "input_too_large",
        DecisionModelOutcome.ScannerUnavailable => "scanner_unavailable",
        DecisionModelOutcome.ContentUnavailable => "content_unavailable",
        _ => "unknown",
    };
}
