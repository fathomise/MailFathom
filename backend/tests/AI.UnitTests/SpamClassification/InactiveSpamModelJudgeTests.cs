// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.SpamClassification;
using MailFathom.Application.Spam.Model;
using Xunit;

namespace MailFathom.AI.UnitTests.SpamClassification;

/// <summary>Covers what a deployment that declared no decision model for spam classification asks, which is nothing at all.</summary>
public sealed class InactiveSpamModelJudgeTests
{
    /// <summary>What the stage reads before it reads any evidence, so a deployment asking nothing gathers nothing.</summary>
    [Fact]
    public void IsActive_ADeploymentWithoutADecisionModel_SaysSo()
    {
        // Act & Assert
        Assert.False(InactiveSpamModelJudge.Instance.IsActive);
    }

    [Theory]
    [InlineData(SpamModelJob.DecideVerdict)]
    [InlineData(SpamModelJob.NameSubcategory)]
    public async Task JudgeAsync_EitherJob_AnswersNotActivatedRatherThanFailing(SpamModelJob job)
    {
        // Act
        var judgement = await InactiveSpamModelJudge.Instance.JudgeAsync(
            SpamClassificationTurnTests.Evidence(),
            job,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamModelOutcome.NotActivated, judgement.Outcome);
        Assert.Null(judgement.Answer);
    }

    [Fact]
    public async Task JudgeAsync_ACancelledCaller_StopsRatherThanAnswering()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            InactiveSpamModelJudge.Instance.JudgeAsync(SpamClassificationTurnTests.Evidence(), SpamModelJob.DecideVerdict, cancellation.Token));
    }
}
