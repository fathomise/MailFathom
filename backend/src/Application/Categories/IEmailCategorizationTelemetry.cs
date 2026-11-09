// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Decisions;

namespace MailFathom.Application.Categories;

/// <summary>Counts how each consultation of categorization ended, without describing the message it was about.</summary>
/// <remarks>A category or a probability is a judgement about one person's message, so the outcome is the whole of what is recorded.</remarks>
public interface IEmailCategorizationTelemetry
{
    /// <summary>Records that one consultation ended.</summary>
    /// <param name="outcome">How it ended.</param>
    void RecordConsultation(DecisionModelOutcome outcome);
}
