// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Evaluations.Judging;

/// <summary>A judge's provider gave no answer at all, after the run had asked it as many times as a busy provider is asked.</summary>
/// <remarks>
/// Raised beneath the run's response cache, by <see cref="AnonymousJudgeChatClient" />, so nothing is filed for the call
/// and a later run asks the judge again. <see cref="JudgePanelEvaluator" /> catches it for every judge of the panel, whose
/// silence leaves its ratings unrated rather than failing the panel. The original failure is deliberately not kept as the
/// inner exception, because its message and every inner one are where a provider names the model and the address.
/// </remarks>
/// <param name="recordedModel">The name the judge is recorded under, its role and never its model.</param>
/// <param name="failure">The failure's type and HTTP status, such as <c>ClientResultException 429</c>, and never its message.</param>
[SuppressMessage(
    "Design",
    "CA1064:Exceptions should be public",
    Justification = "A signal between the judge's client and the panel of this one harness, which nothing outside the evaluation suite raises or catches.")]
internal sealed class JudgeDidNotAnswerException(string recordedModel, string failure)
    : Exception($"The {recordedModel} did not answer ({failure}).");
