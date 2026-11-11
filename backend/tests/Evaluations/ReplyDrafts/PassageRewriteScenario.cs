// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.Orchestration;
using MailFathom.AI.ReplyDrafts;
using MailFathom.Evaluations.StructuredAnswers;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailFathom.Evaluations.ReplyDrafts;

/// <summary>Puts one draft's selected passages to the passage-rewriting agent and holds the rewriting to what the case states.</summary>
/// <remarks>
/// The turn is the one a composer's rewriting composes and the answer is read through <see cref="PassageRewriteReading" />,
/// so an answer the reading refuses — a text too few, a blank one — is a shortfall here exactly as it is nothing in the
/// composer.
/// </remarks>
internal static class PassageRewriteScenario
{
    /// <summary>The name every case's scenario name begins with.</summary>
    public const string Name = "PassageRewrite";

    /// <summary>The name the deterministic verdict is recorded under.</summary>
    public const string ExpectationMetricName = "Rewriting as expected";

    /// <summary>Describes one case as the request the shared scenario runs.</summary>
    /// <param name="scenario">The case.</param>
    /// <returns>The request.</returns>
    public static StructuredAnswerRequest RequestFor(PassageRewriteCase scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        return new StructuredAnswerRequest(
            $"{Name}.{scenario.Name}",
            static constrainedBySchema => PassageRewriteInstructions.TextFor(constrainedBySchema),
            (_, cancellationToken) => ReplyDraftAgent.ComposeRewritingTurnAsync(
                scenario.Request(),
                SensitiveContentEgressGuards.Inactive(),
                cancellationToken),
            static (model, plan) => ReplyDraftAgentComposition.ComposeRewriting(
                model,
                plan,
                new EmptyAgentInstructionEnvelope(),
                NullLoggerFactory.Instance),
            (answer, _) => PassageRewriteReading.Read(answer, scenario.Passages.Count) is { WasWritten: true } rewrite
                ? scenario.Expectation(rewrite.Passages)
                : "nothing could be read from the answer as one text for every passage, so the draft would be left as it was.",
            ExpectationMetricName,
            StructuredAnswerScenario.JudgedWhen(scenario.ReadsTwoWays));
    }
}
