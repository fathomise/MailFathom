// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Persistence;
using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Actions;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.Rules.Editing;
using MailFathom.Application.Rules.Sets;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules.Editing;

/// <summary>
/// Covers every write to an account's rules: each one takes the lock, refuses a stale version, judges the whole
/// candidate, and commits exactly one archive row naming who wrote it — or changes nothing at all.
/// </summary>
public sealed class MailRuleSetEditorTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly MailFolderAlias Archive = MailFolderAlias.Create("archive");

    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

    private static readonly UserId Writer = SyntheticUser.Deployment;

    private readonly InMemoryMailRuleSetStore store = new();

    private readonly IMailRuleActionPermissionReader permissions = Substitute.For<IMailRuleActionPermissionReader>();

    private readonly StubMailFolderMappings folderMappings = StubMailFolderMappings.Nothing;

    private readonly FakeTimeProvider timeProvider = new(Now);

    public static TheoryData<AssignmentScope> ScopesCoveringTheAccount => [.. AccessAuthorizations.ScopesCoveringTheirTarget];

    public static TheoryData<AssignmentScope> ScopesOutsideTheAccount => [.. AccessAuthorizations.ScopesOutsideTheirTarget];

    public MailRuleSetEditorTests()
    {
        this.permissions.GetRuleActionPermissions(Arg.Any<MailAccountId>()).Returns(MailRuleActionPermissions.Default);
        this.folderMappings.With(Account, MailFolderMapping.ToRemotePath(Archive, RemoteFolderPath.Create("INBOX/archive")));
    }

    /// <summary>A created rule never acts on mail before somebody enables it, so it is committed disabled and at the end.</summary>
    [Fact]
    public async Task CreateAsync_ADraft_AppendsTheRuleDisabledAndArchivesTheWrite()
    {
        // Arrange
        var existing = Declared("file-invoices");
        this.store.Arrange(new StoredMailRuleSet(Account, 3, [existing]));

        // Act
        var result = await this.CreateEditor().CreateAsync(
            new MailRuleSetWrite(Account, ExpectedVersion: 3),
            Draft("  mark-newsletters  "),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRuleSetChangeOutcome.Committed, result.Outcome);
        Assert.Equal(4, result.Version);
        Assert.Equal("mark-newsletters", result.Name);
        Assert.Empty(result.Errors);

        var stored = await this.store.ReadAsync(Account, TestContext.Current.CancellationToken);
        Assert.Equal([existing.Id, result.Rule], stored.Declarations.Select(rule => rule.Id));
        var created = stored.Declarations[1];
        Assert.False(created.Enabled);
        Assert.Equal("mark-newsletters", created.Name);
        Assert.Equal(1, this.store.LockCount);

        var archived = Assert.Single(this.store.Archive);
        Assert.Equal(
            (4L, result.Revision, new StoredMailRuleSet(Account, 3, [existing]).Revision, (UserId?)Writer, MailRuleChangeSurface.Administration, Now),
            (archived.Version, archived.Revision, archived.ReplacedRevision, archived.Actor, archived.Surface, archived.CommittedAt));
    }

    /// <summary>A disabled rule is outside the set's identity, so creating one leaves the revision a pass runs under as it was.</summary>
    [Fact]
    public async Task CreateAsync_ADraft_LeavesTheRevisionOfTheEnabledRulesUnchanged()
    {
        // Arrange
        var before = new StoredMailRuleSet(Account, 1, [Declared("file-invoices")]);
        this.store.Arrange(before);

        // Act
        var result = await this.CreateEditor().CreateAsync(
            new MailRuleSetWrite(Account, ExpectedVersion: 1),
            Draft("mark-newsletters"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(before.Revision, result.Revision);
    }

    /// <summary>A writer that read an older version would overwrite a set it never saw, so its write is refused whole.</summary>
    [Fact]
    public async Task CreateAsync_AStaleVersion_ChangesNothingAndReportsTheCurrentVersion()
    {
        // Arrange
        var current = new StoredMailRuleSet(Account, 5, [Declared("file-invoices")]);
        this.store.Arrange(current);

        // Act
        var result = await this.CreateEditor().CreateAsync(
            new MailRuleSetWrite(Account, ExpectedVersion: 4),
            Draft("mark-newsletters"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            (MailRuleSetChangeOutcome.VersionConflict, 5L, current.Revision),
            (result.Outcome, result.Version, result.Revision));
        Assert.Empty(this.store.Archive);
        Assert.Equal(current, await this.store.ReadAsync(Account, TestContext.Current.CancellationToken));
    }

    /// <summary>The version is read under the lock, so a write another replica committed first is seen and wins.</summary>
    [Fact]
    public async Task CreateAsync_AWriteCommittedBeforeTheLockWasTaken_IsAVersionConflict()
    {
        // Arrange
        this.store.Arrange(new StoredMailRuleSet(Account, 1, []));
        var competing = new StoredMailRuleSet(Account, 2, [Declared("written-elsewhere")]);
        this.store.WhenLocked = () => this.store.Arrange(competing);

        // Act
        var result = await this.CreateEditor().CreateAsync(
            new MailRuleSetWrite(Account, ExpectedVersion: 1),
            Draft("mark-newsletters"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((MailRuleSetChangeOutcome.VersionConflict, 2L), (result.Outcome, result.Version));
        Assert.Equal(competing, await this.store.ReadAsync(Account, TestContext.Current.CancellationToken));
        Assert.Empty(this.store.Archive);
    }

    /// <summary>A rule whose condition never compiles must not be stored, disabled or not, so a later enable cannot be the first to find out.</summary>
    [Fact]
    public async Task CreateAsync_AConditionThatDoesNotCompile_IsRefusedAndCommitsNothing()
    {
        // Arrange
        this.store.Arrange(StoredMailRuleSet.Empty(Account));

        // Act
        var result = await this.CreateEditor().CreateAsync(
            new MailRuleSetWrite(Account, ExpectedVersion: 0),
            Draft("broken") with { ConditionText = $"{ScriptedMailRuleConditionCompiler.UnknownFact} == 1" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((MailRuleSetChangeOutcome.Refused, 0L), (result.Outcome, result.Version));
        Assert.NotEmpty(result.Errors);
        Assert.Empty(this.store.Archive);
        Assert.Empty((await this.store.ReadAsync(Account, TestContext.Current.CancellationToken)).Declarations);
    }

    /// <summary>What an account's <c>RuleActions</c> withholds is a ceiling no rule write may pass, whichever surface wrote it.</summary>
    [Fact]
    public async Task CreateAsync_AnActionTheAccountDoesNotPermit_IsRefused()
    {
        // Arrange
        this.store.Arrange(StoredMailRuleSet.Empty(Account));

        // Act
        var result = await this.CreateEditor().CreateAsync(
            new MailRuleSetWrite(Account, ExpectedVersion: 0),
            Draft("delete-it") with { Actions = [MailRuleAction.Delete()] },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRuleSetChangeOutcome.Refused, result.Outcome);
        Assert.Contains(result.Errors, error => error.Contains("RuleActions", StringComparison.Ordinal));
        Assert.Empty(this.store.Archive);
    }

    /// <summary>A destination is a folder the account maps, or the rule is refused rather than left to fail on every match.</summary>
    [Fact]
    public async Task CreateAsync_ADestinationTheAccountDoesNotMap_IsRefused()
    {
        // Arrange
        this.store.Arrange(StoredMailRuleSet.Empty(Account));

        // Act
        var result = await this.CreateEditor().CreateAsync(
            new MailRuleSetWrite(Account, ExpectedVersion: 0),
            Draft("file-it") with { Actions = [MailRuleAction.Relocate(MailFolderReference.ToAlias(MailFolderAlias.Create("nowhere")))] },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRuleSetChangeOutcome.Refused, result.Outcome);
        Assert.Empty(this.store.Archive);
    }

    /// <summary>A replacement is the rule's new declaration, but its identity, its position, and whether it runs are not the draft's to change.</summary>
    [Fact]
    public async Task ReplaceAsync_ARenamedRule_KeepsItsIdentityPositionAndState()
    {
        // Arrange
        var first = Declared("file-invoices");
        var second = Declared("mark-newsletters") with { Enabled = false };
        this.store.Arrange(new StoredMailRuleSet(Account, 2, [first, second]));

        // Act
        var result = await this.CreateEditor().ReplaceAsync(
            new MailRuleSetWrite(Account, ExpectedVersion: 2),
            MailRuleReference.To(first.Id),
            Draft("file-supplier-invoices"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((MailRuleSetChangeOutcome.Committed, 3L, first.Id), (result.Outcome, result.Version, result.Rule));

        var stored = await this.store.ReadAsync(Account, TestContext.Current.CancellationToken);
        Assert.Equal([first.Id, second.Id], stored.Declarations.Select(rule => rule.Id));
        Assert.Equal(("file-supplier-invoices", true), (stored.Declarations[0].Name, stored.Declarations[0].Enabled));
        Assert.Equal(second, stored.Declarations[1]);
    }

    /// <summary>A write naming a rule the set no longer carries is answered as such, and nothing is written in its place.</summary>
    [Fact]
    public async Task ReplaceAsync_ARuleTheSetDoesNotCarry_ReportsRuleNotFound()
    {
        // Arrange
        this.store.Arrange(new StoredMailRuleSet(Account, 1, [Declared("file-invoices")]));

        // Act
        var result = await this.CreateEditor().ReplaceAsync(
            new MailRuleSetWrite(Account, ExpectedVersion: 1),
            MailRuleReference.To(MailRuleIds.For("removed-meanwhile")),
            Draft("file-invoices"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((MailRuleSetChangeOutcome.RuleNotFound, 1L), (result.Outcome, result.Version));
        Assert.Empty(this.store.Archive);
    }

    /// <summary>A person names a rule by what they read, so a name resolves against the locked set without regard to case.</summary>
    [Fact]
    public async Task SetEnabledAsync_ARuleNamedByItsName_ResolvesItInTheLockedSetAndReportsItsIdentity()
    {
        // Arrange
        var first = Declared("file-invoices");
        var second = Declared("mark-newsletters") with { Enabled = false };
        this.store.Arrange(new StoredMailRuleSet(Account, 3, [first, second]));
        Assert.True(MailRuleReference.TryCreate("  Mark-Newsletters ", out var reference));

        // Act
        var result = await this.CreateEditor().SetEnabledAsync(
            new MailRuleSetWrite(Account, ExpectedVersion: 3),
            reference,
            enabled: true,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            (MailRuleSetChangeOutcome.Committed, second.Id, "mark-newsletters"),
            (result.Outcome, result.Rule, result.Name));
        Assert.True((await this.store.ReadAsync(Account, TestContext.Current.CancellationToken)).Declarations[1].Enabled);
    }

    /// <summary>A name that matches no rule is a rule the set does not carry, and nothing about another rule changes.</summary>
    [Fact]
    public async Task RemoveAsync_ANameNoRuleCarries_ReportsRuleNotFoundAndCommitsNothing()
    {
        // Arrange
        this.store.Arrange(new StoredMailRuleSet(Account, 1, [Declared("file-invoices")]));
        Assert.True(MailRuleReference.TryCreate("file-invoice", out var reference));

        // Act
        var result = await this.CreateEditor().RemoveAsync(
            new MailRuleSetWrite(Account, ExpectedVersion: 1),
            reference,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((MailRuleSetChangeOutcome.RuleNotFound, string.Empty), (result.Outcome, result.Name));
        Assert.Empty(this.store.Archive);
    }

    /// <summary>Enabling a rule is what makes it part of what a pass runs, so the revision moves with it.</summary>
    [Fact]
    public async Task SetEnabledAsync_ADisabledRule_EnablesItAndMovesTheRevision()
    {
        // Arrange
        var rule = Declared("file-invoices") with { Enabled = false };
        var before = new StoredMailRuleSet(Account, 1, [rule]);
        this.store.Arrange(before);

        // Act
        var result = await this.CreateEditor().SetEnabledAsync(
            new MailRuleSetWrite(Account, ExpectedVersion: 1),
            MailRuleReference.To(rule.Id),
            enabled: true,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRuleSetChangeOutcome.Committed, result.Outcome);
        Assert.NotEqual(before.Revision, result.Revision);
        Assert.True(Assert.Single((await this.store.ReadAsync(Account, TestContext.Current.CancellationToken)).Declarations).Enabled);
    }

    /// <summary>The order is what a pass evaluates in, so a move places the rule exactly where it was asked to go.</summary>
    [Fact]
    public async Task MoveAsync_TheLastRuleToTheFront_ReordersTheSet()
    {
        // Arrange
        var first = Declared("first");
        var second = Declared("second");
        var third = Declared("third");
        this.store.Arrange(new StoredMailRuleSet(Account, 1, [first, second, third]));

        // Act
        var result = await this.CreateEditor().MoveAsync(
            new MailRuleSetWrite(Account, ExpectedVersion: 1),
            MailRuleReference.To(third.Id),
            position: 0,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRuleSetChangeOutcome.Committed, result.Outcome);
        Assert.Equal(
            [third.Id, first.Id, second.Id],
            (await this.store.ReadAsync(Account, TestContext.Current.CancellationToken)).Declarations.Select(rule => rule.Id));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public async Task MoveAsync_APositionOutsideTheSet_IsRefusedAndCommitsNothing(int position)
    {
        // Arrange
        var first = Declared("first");
        this.store.Arrange(new StoredMailRuleSet(Account, 1, [first, Declared("second")]));

        // Act
        var result = await this.CreateEditor().MoveAsync(
            new MailRuleSetWrite(Account, ExpectedVersion: 1),
            MailRuleReference.To(first.Id),
            position,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRuleSetChangeOutcome.Refused, result.Outcome);
        Assert.Single(result.Errors);
        Assert.Empty(this.store.Archive);
    }

    /// <summary>A removed rule leaves no tombstone in the set: the rules below it move up, and the archive is what remembers it.</summary>
    [Fact]
    public async Task RemoveAsync_ARule_LeavesTheOthersInOrderAndArchivesTheSetWithoutIt()
    {
        // Arrange
        var first = Declared("first");
        var second = Declared("second");
        var third = Declared("third");
        var before = new StoredMailRuleSet(Account, 4, [first, second, third]);
        this.store.Arrange(before);

        // Act
        var result = await this.CreateEditor().RemoveAsync(
            new MailRuleSetWrite(Account, ExpectedVersion: 4),
            MailRuleReference.To(second.Id),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((MailRuleSetChangeOutcome.Committed, 5L), (result.Outcome, result.Version));
        Assert.Equal(
            [first.Id, third.Id],
            (await this.store.ReadAsync(Account, TestContext.Current.CancellationToken)).Declarations.Select(rule => rule.Id));

        var archived = Assert.Single(this.store.Archive);
        Assert.Equal(before.Revision, archived.ReplacedRevision);
        Assert.Equal([first.Id, third.Id], archived.Declarations.Select(rule => rule.Id));
    }

    /// <summary>Reading an account's rules is not writing them, so a caller holding only the read is refused before the lock is taken.</summary>
    [Fact]
    public async Task RemoveAsync_ACallerWithoutConfigurationWrite_IsRefusedBeforeTakingTheLock()
    {
        // Arrange
        var rule = Declared("file-invoices");
        this.store.Arrange(new StoredMailRuleSet(Account, 1, [rule]));
        var editor = this.CreateEditor(AccessAuthorizations.ForUserGranted(Writer, MailFathomPermission.AdminRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() => editor.RemoveAsync(
            new MailRuleSetWrite(Account, ExpectedVersion: 1),
            MailRuleReference.To(rule.Id),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminConfigurationWrite, refusal.RequiredPermission);
        Assert.Equal(0, this.store.LockCount);
        Assert.Empty(this.store.Archive);
    }

    /// <summary>The write grant is judged over the account the write names, so an administrator whose scope covers it writes.</summary>
    [Theory]
    [MemberData(nameof(ScopesCoveringTheAccount))]
    public async Task RemoveAsync_AnAdministratorScopedOverTheAccount_CommitsTheWrite(AssignmentScope scope)
    {
        // Arrange
        var rule = Declared("file-invoices");
        this.store.Arrange(new StoredMailRuleSet(AccessAuthorizations.ScopedAccount, 1, [rule]));
        var editor = this.CreateEditor(AccessAuthorizations.ForAdministratorScopedAt(scope, MailFathomPermission.AdminConfigurationWrite));

        // Act
        var result = await editor.RemoveAsync(
            new MailRuleSetWrite(AccessAuthorizations.ScopedAccount, ExpectedVersion: 1),
            MailRuleReference.To(rule.Id),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRuleSetChangeOutcome.Committed, result.Outcome);
        Assert.Single(this.store.Archive);
    }

    /// <summary>A write grant over another account or organization is no grant over this one, and nothing is locked or written.</summary>
    [Theory]
    [MemberData(nameof(ScopesOutsideTheAccount))]
    public async Task RemoveAsync_AnAdministratorScopedElsewhere_IsRefusedBeforeTakingTheLock(AssignmentScope scope)
    {
        // Arrange
        var rule = Declared("file-invoices");
        this.store.Arrange(new StoredMailRuleSet(AccessAuthorizations.ScopedAccount, 1, [rule]));
        var editor = this.CreateEditor(AccessAuthorizations.ForAdministratorScopedAt(scope, MailFathomPermission.AdminConfigurationWrite));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() => editor.RemoveAsync(
            new MailRuleSetWrite(AccessAuthorizations.ScopedAccount, ExpectedVersion: 1),
            MailRuleReference.To(rule.Id),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminConfigurationWrite, refusal.RequiredPermission);
        Assert.Equal(0, this.store.LockCount);
        Assert.Empty(this.store.Archive);
    }

    private static MailRuleDraft Draft(string name) => new(
        name,
        "isSeen",
        Actions: [MailRuleAction.Relocate(MailFolderReference.ToAlias(Archive))],
        StopWhenMatched: false,
        Triggers: [MailRuleTrigger.Arrival],
        Schedule: null);

    private static MailRuleDeclaration Declared(string name) => new(
        MailRuleIds.For(name),
        name,
        Enabled: true,
        "isSeen",
        Actions: [],
        StopWhenMatched: false,
        Triggers: [MailRuleTrigger.Arrival],
        Schedule: null);

    private MailRuleSetEditor CreateEditor(AccessAuthorization? authorization = null)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return new MailRuleSetEditor(
            this.store,
            new MailRuleSetValidator(new ScriptedMailRuleConditionCompiler(), this.permissions, this.folderMappings.Resolver),
            MailRuleConditionBounds.Default,
            new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), this.timeProvider),
            authorization ?? AccessAuthorizations.ForUserGranted(Writer, MailFathomPermission.AdminConfigurationWrite),
            this.timeProvider);
    }

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
