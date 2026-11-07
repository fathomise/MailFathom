// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Evaluations.Judging;
using Xunit;

namespace MailFathom.Evaluations.UnitTests.Judging;

public sealed class JudgePanelTests
{
    private const string ApiKey = "planted-judge-key";

    private static readonly Uri JudgeAddress = new("https://planted-judge-host.invalid/v1/");

    [Fact]
    public void Of_JurorsAndAnArbiter_SeatsEachUnderItsRoleWithAKeyOfItsOwn()
    {
        // Act
        var panel = JudgePanel.Of(JudgeAddress, ["model-a", "model-b"], "model-c", ApiKey, reasoningEffort: null);

        // Assert
        Assert.Equal(["juror 1", "juror 2", "arbiter"], panel.Judges.Select(static judge => judge.Role));
        Assert.Equal(3, panel.Judges.Select(static judge => judge.CachingKey).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Of_AModelMovedFromOneSeatToAnother_KeepsTheKeyItsVerdictsAreFiledUnder()
    {
        // Act
        var before = JudgePanel.Of(JudgeAddress, ["model-a", "model-b"], arbiterModel: null, ApiKey, reasoningEffort: null);
        var after = JudgePanel.Of(JudgeAddress, ["model-b"], "model-a", ApiKey, reasoningEffort: null);

        // Assert
        Assert.Equal(before.Jurors[0].CachingKey, after.Arbiter?.CachingKey);
        Assert.Equal(before.Jurors[1].CachingKey, after.Jurors[0].CachingKey);
    }

    [Theory]
    [InlineData(new string[0], null, "MAILFATHOM_JUDGE_MODELS")]
    [InlineData(new[] { "planted-a", "planted-b", "planted-c", "planted-d" }, null, "MAILFATHOM_JUDGE_MODELS")]
    [InlineData(new[] { "planted-a", "planted-a" }, null, "MAILFATHOM_JUDGE_MODELS")]
    [InlineData(new[] { "planted-a", "planted-b" }, "planted-b", "MAILFATHOM_JUDGE_ARBITER_MODEL")]
    public void Of_APanelThatWouldNotJudgeAsDeclared_IsRefusedNamingTheVariableAndNeverAModel(
        string[] jurorModels,
        string? arbiterModel,
        string variable)
    {
        // Act
        var refusal = Assert.Throws<InvalidOperationException>(
            () => JudgePanel.Of(JudgeAddress, jurorModels, arbiterModel, ApiKey, reasoningEffort: null));

        // Assert
        Assert.Contains(variable, refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("planted-", refusal.Message, StringComparison.Ordinal);
    }
}
