// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Decisions;

namespace MailFathom.Application.Categories;

/// <summary>What the category half of one batch did, in counts alone.</summary>
/// <param name="CategorizedEmailCount">How many messages a model answered about.</param>
/// <param name="CurrentEmailCount">How many it passed over because they already answer the questions as they stand.</param>
/// <param name="UnansweredEmailCount">How many it had no answer for, whether asked or not asked because asking had stopped.</param>
/// <param name="ClearedEmailCount">How many lost their categories because they are spam.</param>
/// <param name="AskingStoppedBy">What stopped this pass asking, where something has.</param>
public sealed record StoredEmailCategorizationReport(
    int CategorizedEmailCount,
    int CurrentEmailCount,
    int UnansweredEmailCount,
    int ClearedEmailCount,
    DecisionModelOutcome? AskingStoppedBy);
