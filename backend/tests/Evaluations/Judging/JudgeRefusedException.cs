// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Evaluations.Judging;

/// <summary>A judge's provider refused to answer a request rather than failing to reach it.</summary>
/// <remarks>
/// Raised beneath the run's response cache, by <see cref="AnonymousJudgeChatClient" />, so a refusal is never filed as an
/// answer and a later run asks the judge again. <see cref="JudgePanelEvaluator" /> catches it for the arbiter, whose
/// refusal leaves the jurors to decide; a juror's refusal travels on and fails that evaluator, as a juror that did not
/// answer does.
/// </remarks>
/// <param name="recordedModel">The name the judge is recorded under, its role and never its model.</param>
[SuppressMessage(
    "Design",
    "CA1064:Exceptions should be public",
    Justification = "A signal between the judge's client and the panel of this one harness, which nothing outside the evaluation suite raises or catches.")]
internal sealed class JudgeRefusedException(string recordedModel)
    : Exception($"The {recordedModel} refused to answer.");
