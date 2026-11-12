// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.Chat;
using MailFathom.Evaluations.StructuredAnswers;
using Xunit;

namespace MailFathom.Evaluations.ReplyDrafts;

/// <summary>Measures what the passage-rewriting agent makes of each selected passage, under every model declared for drafting.</summary>
/// <remarks>A theory over the cases for the reason <c>DiscoveryPlanningEvaluations</c> gives; the models are drafting's, because rewriting is drafting's second act.</remarks>
public sealed class PassageRewriteEvaluations
{
    /// <summary>Gets whether an evaluation run was explicitly asked for.</summary>
    /// <remarks>Public and static because that is the shape xUnit reads a skip condition from.</remarks>
    public static bool EvaluationsRequested => AiEvaluationRun.Requested;

    /// <summary>Gets every case, by the name it is filed under.</summary>
    public static TheoryData<string> Cases { get; } = new(PassageRewriteCase.All.Select(static scenario => scenario.Name));

    [Theory(Skip = AiEvaluationRun.SkipReason, SkipUnless = nameof(EvaluationsRequested))]
    [MemberData(nameof(Cases))]
    public async Task RewritePassages_ASelectedPassage_EveryDeclaredModelRewritesItAsAskedAndKeepsWhatItStates(string caseName)
    {
        // Arrange
        var request = PassageRewriteScenario.RequestFor(PassageRewriteCase.Named(caseName));

        // Act
        var shortfalls = await StructuredAnswerScenario.MeasureEveryDeclaredModelAsync(
            ChatCapability.ReplyDrafting,
            request,
            TestContext.Current.CancellationToken);

        // Assert
        AiEvaluationRun.AssertNoShortfalls(shortfalls);
    }
}
