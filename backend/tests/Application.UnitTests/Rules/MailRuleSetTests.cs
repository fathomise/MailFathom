// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Jobs.Scheduling;
using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules;

/// <summary>Covers what a bound rule set refuses to be, and the ordering it promises to keep.</summary>
public sealed class MailRuleSetTests
{
    private static readonly MailRuleSetRevision Revision =
        MailRuleSetRevision.Create(
        [
            new MailRuleDeclaration(
                MailRuleIds.For("rule"),
                "rule",
                Enabled: true,
                "isSeen",
                Actions: [],
                StopWhenMatched: false,
                Triggers: [MailRuleTrigger.Arrival],
                Schedule: null),
        ]);

    [Fact]
    public void Create_Rules_KeepsThemInTheOrderTheyWereDeclared()
    {
        // Arrange
        var rules = Enumerable
            .Range(0, 4)
            .Select(position => CreateRule($"rule-{position}"))
            .ToArray();

        // Act
        var ruleSet = MailRuleSet.Create(rules, Revision, MailRuleConditionBounds.Default);

        // Assert
        Assert.Equal(
            ["rule-0", "rule-1", "rule-2", "rule-3"],
            ruleSet.Rules.Select(rule => rule.Name));
        Assert.False(ruleSet.IsEmpty);
    }

    [Fact]
    public void Create_NoRules_IsAnEmptySetRatherThanARefusal()
    {
        // Act
        var ruleSet = MailRuleSet.Create([], Revision, MailRuleConditionBounds.Default);

        // Assert
        Assert.True(ruleSet.IsEmpty);
        Assert.Equal(Revision, ruleSet.Revision);
    }

    /// <summary>A person reads a rule by its name, so two rules answering to one name could not be told apart.</summary>
    [Theory]
    [InlineData("duplicate")]
    [InlineData("DUPLICATE")]
    public void Create_TwoRulesUnderOneName_IsRefused(string secondName)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(
            () => MailRuleSet.Create([CreateRule("duplicate"), CreateRule(secondName, "second")], Revision, MailRuleConditionBounds.Default));
    }

    /// <summary>A rule's identifier is what every record names it by, so two rules under one would be one rule twice.</summary>
    [Fact]
    public void Create_TwoRulesUnderOneIdentifier_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(
            () => MailRuleSet.Create([CreateRule("first", "shared"), CreateRule("second", "shared")], Revision, MailRuleConditionBounds.Default));
    }

    [Fact]
    public void Create_RuleWithoutAnIdentifier_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(
            () => MailRule.Create(default, "rule", ScriptedMailRuleCondition.Answering(matches: true)));
    }

    /// <summary>A held set carries no rule a pass could run, and names the ones that hold it.</summary>
    [Fact]
    public void Held_RulesThatNoLongerCompile_IsHeldAndEmpty()
    {
        // Act
        var ruleSet = MailRuleSet.Held(Revision, MailRuleConditionBounds.Default, [MailRuleIds.For("broken")]);

        // Assert
        Assert.True(ruleSet.IsHeld);
        Assert.Empty(ruleSet.Rules);
        Assert.Equal([MailRuleIds.For("broken")], ruleSet.UncompiledRules);
    }

    [Fact]
    public void Create_WithoutADerivedRevision_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => MailRuleSet.Create([CreateRule("rule")], default, MailRuleConditionBounds.Default));
    }

    [Fact]
    public void Create_Rules_CannotBeChangedThroughTheListItWasGiven()
    {
        // Arrange
        var rules = new List<MailRule> { CreateRule("rule") };
        var ruleSet = MailRuleSet.Create(rules, Revision, MailRuleConditionBounds.Default);

        // Act
        rules.Add(CreateRule("added-afterwards"));

        // Assert
        Assert.Single(ruleSet.Rules);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RuleWithoutAName_IsRefused(string? name)
    {
        // Act, Assert
        Assert.ThrowsAny<ArgumentException>(
            () => MailRule.Create(MailRuleIds.For("rule"), name!, ScriptedMailRuleCondition.Answering(matches: true), stopWhenMatched: false));
    }

    [Fact]
    public void Create_RuleWithoutACondition_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => MailRule.Create(MailRuleIds.For("rule"), "rule", null!, stopWhenMatched: false));
    }

    /// <summary>The schedule and the trigger are one declaration, so a rule carrying one without the other is refused.</summary>
    [Fact]
    public void Create_ARuleDeclaringTheScheduleTriggerWithoutASchedule_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => MailRule.Create(
            MailRuleIds.For("nightly"),
            "nightly",
            ScriptedMailRuleCondition.Answering(matches: true),
            triggers: [MailRuleTrigger.Schedule]));
    }

    /// <summary>A schedule nothing fires would be an occasion an operator declared and the system never keeps.</summary>
    [Fact]
    public void Create_ARuleCarryingAScheduleItDeclaresNoTriggerFor_IsRefused()
    {
        // Arrange
        Assert.True(JobRecurrence.TryParse("Daily at 03:00", out var recurrence, out _));

        // Act, Assert
        Assert.Throws<ArgumentException>(() => MailRule.Create(
            MailRuleIds.For("nightly"),
            "nightly",
            ScriptedMailRuleCondition.Answering(matches: true),
            triggers: [MailRuleTrigger.Arrival],
            schedule: recurrence));
    }

    private static MailRule CreateRule(string name, string? identity = null) =>
        MailRule.Create(MailRuleIds.For(identity ?? name), name, ScriptedMailRuleCondition.Answering(matches: true), stopWhenMatched: false);
}
