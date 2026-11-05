// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Jobs.Scheduling;
using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Actions;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.Rules.Editing;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules.Editing;

/// <summary>
/// Covers the one judgement every write to an account's rules passes: the set as a whole, each rule's declaration, the
/// account's own ceiling on what a rule may do and where it may file, and a refusal that quotes nothing a person typed.
/// </summary>
public sealed class MailRuleSetValidatorTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly MailFolderAlias Archive = MailFolderAlias.Create("archive");

    private readonly IMailRuleActionPermissionReader permissions = Substitute.For<IMailRuleActionPermissionReader>();

    private readonly StubMailFolderMappings folderMappings = StubMailFolderMappings.Nothing;

    private readonly ScriptedMailRuleConditionCompiler compiler = new();

    public MailRuleSetValidatorTests()
    {
        this.permissions.GetRuleActionPermissions(Arg.Any<MailAccountId>()).Returns(MailRuleActionPermissions.Default);
        this.folderMappings
            .With(Account, MailFolderMapping.ToRemotePath(Archive, RemoteFolderPath.Create("INBOX/archive")))
            .With(
                Account,
                MailFolderMapping.ToRemotePath(
                    MailFolderAlias.Create("junk"),
                    RemoteFolderPath.Create("INBOX/junk"),
                    specialUse: MailFolderSpecialUse.Junk));
    }

    [Fact]
    public void FindErrors_ASetEveryRuleOfWhichIsSound_FindsNothing()
    {
        // Arrange
        var candidate = new[]
        {
            Declared("file-invoices") with { Actions = [MailRuleAction.Relocate(MailFolderReference.ToAlias(Archive))] },
            Declared("junk-it") with { Actions = [MailRuleAction.Relocate(MailFolderReference.ToRole(MailFolderSpecialUse.Junk))] },
            Scheduled("nightly"),
        };

        // Act
        var errors = this.FindErrors(candidate);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>The bound is on what the account keeps, so disabled rules count towards it as much as enabled ones.</summary>
    [Fact]
    public void FindErrors_MoreRulesThanAnAccountKeeps_IsRefused()
    {
        // Arrange
        var candidate = Enumerable.Range(0, MailRuleSetValidator.MaximumRulesPerAccount + 1)
            .Select(index => Declared($"rule-{index}") with { Enabled = index % 2 == 0 })
            .ToArray();

        // Act
        var errors = this.FindErrors(candidate);

        // Assert
        Assert.Equal(
            [$"An account keeps at most {MailRuleSetValidator.MaximumRulesPerAccount} rules, and this change would leave it {candidate.Length}."],
            errors);
    }

    /// <summary>A person reads a rule by its name, so two names differing only in case would read as one rule.</summary>
    [Fact]
    public void FindErrors_TwoRulesWhoseNamesDifferOnlyInCase_NamesBothByIdentifier()
    {
        // Arrange
        var first = Declared("File-Invoices");
        var second = Declared("file-invoices");

        // Act
        var error = Assert.Single(this.FindErrors([first, second]));

        // Assert
        Assert.Contains(first.Id.ToString(), error, StringComparison.Ordinal);
        Assert.Contains(second.Id.ToString(), error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindErrors_OneIdentifierTwice_IsRefused()
    {
        // Arrange
        var rule = Declared("file-invoices");

        // Act
        var errors = this.FindErrors([rule, rule with { Name = "file-invoices-again" }]);

        // Assert
        Assert.Equal([$"Rule {rule.Id} appears more than once in the set."], errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tab\tinside")]
    public void FindErrors_ANameEmptyOrCarryingAControlCharacter_IsRefused(string name)
    {
        // Act
        var errors = this.FindErrors([Declared("placeholder") with { Name = name }]);

        // Assert
        Assert.Single(errors);
    }

    [Fact]
    public void FindErrors_ANameLongerThanTheBound_IsRefused()
    {
        // Arrange
        var rule = Declared("placeholder") with { Name = new string('n', MailRuleSetValidator.MaximumNameLength + 1) };

        // Act
        var errors = this.FindErrors([rule]);

        // Assert
        Assert.Equal([$"Rule {rule.Id} needs a name of 1 to {MailRuleSetValidator.MaximumNameLength} characters."], errors);
    }

    /// <summary>A trigger, its schedule, and its uniqueness are decided together, because each half alone runs nothing or runs twice.</summary>
    [Fact]
    public void FindErrors_TriggersThatCannotRunAsDeclared_RefuseEachShape()
    {
        // Arrange
        var repeated = Declared("repeated") with { Triggers = [MailRuleTrigger.Arrival, MailRuleTrigger.Arrival] };
        var unscheduled = Declared("unscheduled") with { Triggers = [MailRuleTrigger.Schedule] };
        var untriggered = Scheduled("untriggered") with { Triggers = [MailRuleTrigger.Arrival] };
        var unspecified = Declared("unspecified") with { Triggers = [default] };
        MailRuleDeclaration[] candidate = [repeated, unscheduled, untriggered, unspecified];

        // Act
        var errors = this.FindErrors(candidate);

        // Assert
        Assert.Equal(candidate.Length, errors.Count);
        Assert.All(candidate, rule => Assert.Single(errors, error => error.Contains(rule.Id.ToString(), StringComparison.Ordinal)));
    }

    /// <summary>A deletion is opt-in per account, so a rule asking for one on an account that never opted in is refused.</summary>
    [Fact]
    public void FindErrors_AnActionTheAccountDoesNotPermit_PointsAtRuleActions()
    {
        // Arrange
        var rule = Declared("delete-it") with { Actions = [MailRuleAction.Delete()] };

        // Act
        var error = Assert.Single(this.FindErrors([rule]));

        // Assert
        Assert.Contains("RuleActions", error, StringComparison.Ordinal);
    }

    /// <summary>The same ceiling holds over a disabled rule, so enabling it later is never the first time it is judged.</summary>
    [Fact]
    public void FindErrors_ADisabledRuleWithAnUnpermittedAction_IsStillRefused()
    {
        // Arrange
        var rule = Declared("delete-it") with { Enabled = false, Actions = [MailRuleAction.Delete()] };

        // Act
        var errors = this.FindErrors([rule]);

        // Assert
        Assert.Single(errors);
    }

    [Fact]
    public void FindErrors_AnActionTheAccountPermits_IsAccepted()
    {
        // Arrange
        this.permissions
            .GetRuleActionPermissions(Account)
            .Returns(MailRuleActionPermissions.Default with { PermitsDelete = true });

        // Act
        var errors = this.FindErrors([Declared("delete-it") with { Actions = [MailRuleAction.Delete()] }]);

        // Assert
        Assert.Empty(errors);
    }

    public static TheoryData<string, MailFolderReference> UnmappedDestinations => new()
    {
        { "an alias the account does not map", MailFolderReference.ToAlias(MailFolderAlias.Create("nowhere")) },
        { "a role no folder of the account plays", MailFolderReference.ToRole(MailFolderSpecialUse.Trash) },
        { "a folder identifier, which a rule cannot file by yet", MailFolderReference.ToFolder(MailFolderId.Create(new Guid("0198f0aa-0000-7000-8000-0000000000aa"))) },
    };

    [Theory]
    [MemberData(nameof(UnmappedDestinations))]
    public void FindErrors_ADestinationTheAccountDoesNotMap_IsRefused(string scenario, MailFolderReference destination)
    {
        // Arrange
        var rule = Declared("file-it") with { Actions = [MailRuleAction.Copy(destination)] };

        // Act
        var errors = this.FindErrors([rule]);

        // Assert
        Assert.True(errors.Count == 1, $"{scenario} should be refused once but reported: {string.Join(" ", errors)}");
    }

    [Fact]
    public void FindErrors_TwoFatesForOneOccurrence_IsRefused()
    {
        // Arrange
        this.permissions
            .GetRuleActionPermissions(Account)
            .Returns(MailRuleActionPermissions.Default with { PermitsDelete = true });
        var rule = Declared("file-and-delete") with
        {
            Actions = [MailRuleAction.Relocate(MailFolderReference.ToAlias(Archive)), MailRuleAction.Delete()],
        };

        // Act
        var errors = this.FindErrors([rule]);

        // Assert
        Assert.Single(errors);
    }

    [Fact]
    public void FindErrors_AConditionThatDoesNotCompile_ReportsWhatTheCompilerFound()
    {
        // Arrange
        var rule = Declared("broken") with { ConditionText = $"{ScriptedMailRuleConditionCompiler.UnknownFact} == 1" };

        // Act
        var errors = this.FindErrors([rule]);

        // Assert
        Assert.Equal([$"Rule {rule.Id} names a fact this deployment does not know."], errors);
    }

    /// <summary>The revision is derived over separated fields, so a condition carrying a separator could make two sets digest alike.</summary>
    [Fact]
    public void FindErrors_AConditionCarryingARevisionSeparator_IsRefusedWithoutBeingCompiled()
    {
        // Arrange
        var rule = Declared("separated") with { ConditionText = "isSeen\u001Fhidden" };

        // Act
        var errors = this.FindErrors([rule]);

        // Assert
        Assert.Single(errors);
        Assert.Equal(0, this.compiler.CompileCount);
    }

    /// <summary>
    /// A narrowed <c>RuleActions</c> is not checked against stored rules, so a rule it broke blocks no write that leaves
    /// the rule as it was stored.
    /// </summary>
    [Fact]
    public void FindErrors_AStoredRuleNoLongerPermittedLeftAsItWas_DoesNotRefuseAnUnrelatedCreate()
    {
        // Arrange
        var deleting = Declared("delete-it") with { Actions = [MailRuleAction.Delete()] };
        var created = Declared("created") with { Enabled = false };

        // Act
        var errors = this.FindErrors([deleting], [deleting, created]);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>Disabling the rule that broke is how a write takes it out of the set's way, so it is not judged again.</summary>
    [Fact]
    public void FindErrors_AStoredRuleNoLongerPermittedBeingDisabled_FindsNothing()
    {
        // Arrange
        var deleting = Declared("delete-it") with { Actions = [MailRuleAction.Delete()] };

        // Act
        var errors = this.FindErrors([deleting], [deleting with { Enabled = false }]);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>Enabling a stored rule is a write that puts it back into every pass, so it is judged as if it were new.</summary>
    [Fact]
    public void FindErrors_AStoredRuleNoLongerPermittedBeingEnabled_IsRefused()
    {
        // Arrange
        var deleting = Declared("delete-it") with { Enabled = false, Actions = [MailRuleAction.Delete()] };

        // Act
        var errors = this.FindErrors([deleting], [deleting with { Enabled = true }]);

        // Assert
        Assert.Single(errors);
    }

    /// <summary>A rule whose content the write changes is judged, whichever field moved.</summary>
    [Fact]
    public void FindErrors_AStoredRuleNoLongerPermittedBeingRenamed_IsRefused()
    {
        // Arrange
        var deleting = Declared("delete-it") with { Actions = [MailRuleAction.Delete()] };

        // Act
        var errors = this.FindErrors([deleting], [deleting with { Name = "renamed" }]);

        // Assert
        Assert.Single(errors);
    }

    /// <summary>A refusal names the rule by identifier and never quotes the name or the folder a person typed.</summary>
    [Fact]
    public void FindErrors_ARefusedRule_QuotesNeitherItsNameNorItsFolder()
    {
        // Arrange
        this.permissions
            .GetRuleActionPermissions(Account)
            .Returns(MailRuleActionPermissions.Default with { PermitsDelete = true });
        var secretFolder = MailFolderAlias.Create("private-folder");
        var secrets = Declared("private-name") with
        {
            ConditionText = $"{ScriptedMailRuleConditionCompiler.UnknownFact} == 'private-condition'",
            Actions =
            [
                MailRuleAction.Relocate(MailFolderReference.ToAlias(secretFolder)),
                MailRuleAction.Copy(MailFolderReference.ToAlias(secretFolder)),
            ],
        };

        // Act
        var errors = this.FindErrors([secrets, secrets with { Id = MailRuleIds.For("twin") }]);

        // Assert
        Assert.NotEmpty(errors);
        Assert.DoesNotContain(errors, error =>
            error.Contains("private", StringComparison.OrdinalIgnoreCase));
    }

    private static MailRuleDeclaration Declared(string name) => new(
        MailRuleIds.For(name),
        name,
        Enabled: true,
        "isSeen",
        Actions: [],
        StopWhenMatched: false,
        Triggers: [MailRuleTrigger.Arrival],
        Schedule: null);

    private static MailRuleDeclaration Scheduled(string name)
    {
        Assert.True(JobRecurrence.TryParse("Daily at 03:00", out var recurrence, out _));

        return Declared(name) with { Triggers = [MailRuleTrigger.Schedule], Schedule = recurrence };
    }

    private IReadOnlyList<string> FindErrors(IReadOnlyList<MailRuleDeclaration> candidate) => this.FindErrors([], candidate);

    private IReadOnlyList<string> FindErrors(
        IReadOnlyList<MailRuleDeclaration> stored,
        IReadOnlyList<MailRuleDeclaration> candidate) =>
        new MailRuleSetValidator(this.compiler, this.permissions, this.folderMappings.Resolver)
            .FindErrors(Account, stored, candidate, MailRuleConditionBounds.Default);
}
