// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Decisions;

namespace MailFathom.Application.Categories;

/// <summary>What the category half of one batch of a run over stored mail is carried under.</summary>
/// <param name="Settings">What the account decided, read for the whole pass.</param>
/// <param name="OwnCategory">The name of the one own category the run applies, or <see langword="null" /> for everything.</param>
/// <param name="Rescores">Whether mail already holding answers to the current questions is asked again.</param>
/// <param name="AskingStoppedBy">What already stopped this pass asking, where something did.</param>
public sealed record StoredEmailCategorizationTerms(
    EmailCategorizationSettings Settings,
    string? OwnCategory,
    bool Rescores,
    DecisionModelOutcome? AskingStoppedBy);
