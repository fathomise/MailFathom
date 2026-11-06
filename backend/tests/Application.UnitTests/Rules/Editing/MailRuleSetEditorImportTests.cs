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
/// Covers an import of a whole set into an account's rules: one candidate matched to the stored set by name, judged as a
/// whole, and committed as one write or not at all — and a preview that judges it the same way and writes nothing.
/// </summary>
public sealed class MailRuleSetEditorImportTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly MailFolderAlias Archive = MailFolderAlias.Create("archive");

    private static readonly DateTimeOffset Now = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    private static readonly UserId Writer = SyntheticUser.Deployment;

    private readonly InMemoryMailRuleSetStore store = new();

    private readonly IMailRuleActionPermissionReader permissions = Substitute.For<IMailRuleActionPermissionReader>();

    private readonly StubMailFolderMappings folderMappings = StubMailFolderMappings.Nothing;

    private readonly FakeTimeProvider timeProvider = new(Now);

    public MailRuleSetEditorImportTests()
    {
        this.permissions.GetRuleActionPermissions(Arg.Any<MailAccountId>()).Returns(MailRuleActionPermissions.Default);
        this.folderMappings.With(Account, MailFolderMapping.ToRemotePath(Archive, RemoteFolderPath.Create("INBOX/archive")));
    }

    /// <summary>An import is the upgrade path from configured rules, and what it creates is still a create: disabled, in the file's order.</summary>
    [Fact]
    public async Task ImportAsync_ReplacingAnEmptyAccount_AddsEveryRuleDisabledInTheImportedOrder()
    {
        // Arrange
        this.store.Arrange(StoredMailRuleSet.Empty(Account));

        // Act
        var result = await this.CreateEditor().ImportAsync(
            new MailRuleSetWrite(Account, ExpectedVersion: 0),
            new MailRuleSetImport(MailRuleSetImportMode.Replace, [Draft("file-invoices"), Draft("mark-newsletters")]),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((MailRuleSetChangeOutcome.Committed, 1L), (result.Outcome, result.Version));
        Assert.Equal(
            [(MailRuleImportEffect.Added, "file-invoices", false), (MailRuleImportEffect.Added, "mark-newsletters", false)],
            result.Rules.Select(rule => (rule.Effect, rule.Name, rule.Enabled)));

        var stored = await this.store.ReadAsync(Account, TestContext.Current.CancellationToken);
        Assert.Equal(result.Rules.Select(rule => rule.Rule), stored.Declarations.Select(rule => rule.Id));
        Assert.All(stored.Declarations, rule => Assert.False(rule.Enabled));

        var archived = Assert.Single(this.store.Archive);
        Assert.Equal((1L, (UserId?)Writer, MailRuleChangeSurface.Administration), (archived.Version, archived.Actor, archived.Surface));
    }

    /// <summary>
    /// A replacement matches by name without regard to case, so a matched rule keeps its identity and whether it runs,
    /// the set takes the file's order, and every rule the file does not name is removed and reported as removed.
    /// </summary>
    [Fact]
    public async Task ImportAsync_Replacing_KeepsAMatchedRulesIdentityAndStateAndRemovesEveryRuleTheImportDoesNotName()
    {
        // Arrange
        var invoices = Declared("file-invoices", enabled: true);
        var newsletters = Declared("mark-newsletters", enabled: true);
        var obsolete = Declared("obsolete", enabled: true);
        this.store.Arrange(new StoredMailRuleSet(Account, 3, [invoices, newsletters, obsolete]));

        // Act
        var result = await this.CreateEditor().ImportAsync(
            new MailRuleSetWrite(Account, ExpectedVersion: 3),
            new MailRuleSetImport(
                MailRuleSetImportMode.Replace,
                [Draft("Mark-Newsletters"), Draft("file-invoices") with { ConditionText = "isFlagged" }, Draft("flag-supplier")]),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRuleSetChangeOutcome.Committed, result.Outcome);
        Assert.Equal(
            [
                (MailRuleImportEffect.Changed, newsletters.Id, true),
                (MailRuleImportEffect.Changed, invoices.Id, true),
                (MailRuleImportEffect.Added, result.Rules[2].Rule, false),
                (MailRuleImportEffect.Removed, obsolete.Id, true),
            ],
            result.Rules.Select(rule => (rule.Effect, rule.Rule, rule.Enabled)));

        var stored = await this.store.ReadAsync(Account, TestContext.Current.CancellationToken);
        Assert.Equal([newsletters.Id, invoices.Id, result.Rules[2].Rule], stored.Declarations.Select(rule => rule.Id));
        Assert.Equal("Mark-Newsletters", stored.Declarations[0].Name);
    }

    /// <summary>A merge keeps every rule the file does not name, replaces a matched rule where it stands, and adds the rest at the end.</summary>
    [Fact]
    public async Task ImportAsync_Merging_ReplacesMatchedRulesInPlaceAddsTheRestAtTheEndAndKeepsEveryOtherRule()
    {
        // Arrange
        var invoices = Declared("file-invoices", enabled: true);
        var newsletters = Declared("mark-newsletters", enabled: false);
        this.store.Arrange(new StoredMailRuleSet(Account, 2, [invoices, newsletters]));

        // Act
        var result = await this.CreateEditor().ImportAsync(
            new MailRuleSetWrite(Account, ExpectedVersion: 2),
            new MailRuleSetImport(
                MailRuleSetImportMode.Merge,
                [Draft("flag-supplier"), Draft("file-invoices") with { StopWhenMatched = true }]),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [(MailRuleImportEffect.Added, false), (MailRuleImportEffect.Changed, true)],
            result.Rules.Select(rule => (rule.Effect, rule.Enabled)));

        var stored = await this.store.ReadAsync(Account, TestContext.Current.CancellationToken);
        Assert.Equal([invoices.Id, newsletters.Id, result.Rules[0].Rule], stored.Declarations.Select(rule => rule.Id));
        Assert.True(stored.Declarations[0].StopWhenMatched);
        Assert.Equal(newsletters, stored.Declarations[1]);
    }

    /// <summary>A rule that declares what it already declared is reported as unchanged, or as moved when the file puts it elsewhere among the rules both keep.</summary>
    [Fact]
    public async Task PreviewImportAsync_TheSameRulesInAnotherOrder_ReportsTheMovedAndTheUnchangedRules()
    {
        // Arrange
        var first = Declared("first", enabled: true);
        var second = Declared("second", enabled: true);
        var third = Declared("third", enabled: true);
        this.store.Arrange(new StoredMailRuleSet(Account, 5, [first, second, third]));

        // Act
        var result = await this.CreateEditor().PreviewImportAsync(
            new MailRuleSetWrite(Account, ExpectedVersion: 5),
            new MailRuleSetImport(MailRuleSetImportMode.Replace, [Redraft(second), Redraft(first), Redraft(third)]),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [(MailRuleImportEffect.Moved, "second"), (MailRuleImportEffect.Moved, "first"), (MailRuleImportEffect.Unchanged, "third")],
            result.Rules.Select(rule => (rule.Effect, rule.Name)));
    }

    /// <summary>A preview answers exactly what the import would, and takes no lock, moves no version, and archives nothing.</summary>
    [Fact]
    public async Task PreviewImportAsync_AnImportThatWouldCommit_ReportsItAndWritesNothing()
    {
        // Arrange
        var before = new StoredMailRuleSet(Account, 4, [Declared("obsolete", enabled: true)]);
        this.store.Arrange(before);

        // Act
        var result = await this.CreateEditor().PreviewImportAsync(
            new MailRuleSetWrite(Account, ExpectedVersion: 4),
            new MailRuleSetImport(MailRuleSetImportMode.Replace, [Draft("flag-supplier")]),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((MailRuleSetChangeOutcome.Previewed, 4L, before.Revision), (result.Outcome, result.Version, result.Revision));
        Assert.Equal([MailRuleImportEffect.Added, MailRuleImportEffect.Removed], result.Rules.Select(rule => rule.Effect));
        Assert.Equal(0, this.store.LockCount);
        Assert.Empty(this.store.Archive);
        Assert.Equal(before, await this.store.ReadAsync(Account, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The whole import is one candidate, so one defective rule refuses all of it; the refusal names the rule by its place
    /// in the import, since the identifier it would have been given was never stored.
    /// </summary>
    [Fact]
    public async Task ImportAsync_OneRuleThatDoesNotCompile_RefusesTheWholeImportNamingTheRuleByItsPlace()
    {
        // Arrange
        var before = new StoredMailRuleSet(Account, 1, [Declared("obsolete", enabled: true)]);
        this.store.Arrange(before);

        // Act
        var result = await this.CreateEditor().ImportAsync(
            new MailRuleSetWrite(Account, ExpectedVersion: 1),
            new MailRuleSetImport(
                MailRuleSetImportMode.Replace,
                [Draft("file-invoices"), Draft("broken") with { ConditionText = $"{ScriptedMailRuleConditionCompiler.UnknownFact} == 1" }]),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((MailRuleSetChangeOutcome.Refused, 1L), (result.Outcome, result.Version));
        Assert.Contains(result.Errors, error => error.Contains("#2", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Errors, error => result.Rules.Any(rule => error.Contains(rule.Rule.ToString(), StringComparison.Ordinal)));
        Assert.Empty(this.store.Archive);
        Assert.Equal(before, await this.store.ReadAsync(Account, TestContext.Current.CancellationToken));
    }

    /// <summary>Which of two rules carrying one name was meant is the file's to say, so such an import is refused before anything is matched.</summary>
    [Fact]
    public async Task ImportAsync_TwoRulesCarryingOneName_IsRefusedNamingBothByTheirPlace()
    {
        // Arrange
        this.store.Arrange(new StoredMailRuleSet(Account, 1, [Declared("file-invoices", enabled: true)]));

        // Act
        var result = await this.CreateEditor().ImportAsync(
            new MailRuleSetWrite(Account, ExpectedVersion: 1),
            new MailRuleSetImport(MailRuleSetImportMode.Merge, [Draft("file-invoices"), Draft("other"), Draft("FILE-INVOICES")]),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRuleSetChangeOutcome.Refused, result.Outcome);
        Assert.Contains("Rules #1 and #3 of the import carry one name", Assert.Single(result.Errors), StringComparison.Ordinal);
        Assert.Empty(result.Rules);
        Assert.Empty(this.store.Archive);
    }

    /// <summary>An import decided against an older version would replace a set its writer never saw, so it is refused whole.</summary>
    [Fact]
    public async Task ImportAsync_AStaleVersion_IsAConflictAndChangesNothing()
    {
        // Arrange
        var current = new StoredMailRuleSet(Account, 6, [Declared("file-invoices", enabled: true)]);
        this.store.Arrange(current);

        // Act
        var result = await this.CreateEditor().ImportAsync(
            new MailRuleSetWrite(Account, ExpectedVersion: 5),
            new MailRuleSetImport(MailRuleSetImportMode.Replace, []),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((MailRuleSetChangeOutcome.VersionConflict, 6L), (result.Outcome, result.Version));
        Assert.Empty(this.store.Archive);
        Assert.Equal(current, await this.store.ReadAsync(Account, TestContext.Current.CancellationToken));
    }

    /// <summary>A preview against a stale version is the conflict the import would meet, so the operator rereads before deciding.</summary>
    [Fact]
    public async Task PreviewImportAsync_AStaleVersion_IsAConflict()
    {
        // Arrange
        this.store.Arrange(new StoredMailRuleSet(Account, 6, []));

        // Act
        var result = await this.CreateEditor().PreviewImportAsync(
            new MailRuleSetWrite(Account, ExpectedVersion: 2),
            new MailRuleSetImport(MailRuleSetImportMode.Merge, [Draft("file-invoices")]),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((MailRuleSetChangeOutcome.VersionConflict, 6L), (result.Outcome, result.Version));
        Assert.Empty(result.Rules);
    }

    /// <summary>An import writes the account's rules, so the read grant that previews one is refused before anything is locked.</summary>
    [Fact]
    public async Task ImportAsync_ACallerHoldingOnlyTheRead_IsRefusedBeforeTakingTheLock()
    {
        // Arrange
        this.store.Arrange(StoredMailRuleSet.Empty(Account));
        var editor = this.CreateEditor(AccessAuthorizations.ForUserGranted(Writer, MailFathomPermission.AdminRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() => editor.ImportAsync(
            new MailRuleSetWrite(Account, ExpectedVersion: 0),
            new MailRuleSetImport(MailRuleSetImportMode.Replace, [Draft("file-invoices")]),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminConfigurationWrite, refusal.RequiredPermission);
        Assert.Equal(0, this.store.LockCount);
        Assert.Empty(this.store.Archive);
    }

    /// <summary>A preview reads the stored rules and judges a candidate, which is what the read grant over the account covers.</summary>
    [Fact]
    public async Task PreviewImportAsync_ACallerHoldingOnlyTheRead_IsAnswered()
    {
        // Arrange
        this.store.Arrange(StoredMailRuleSet.Empty(Account));
        var editor = this.CreateEditor(AccessAuthorizations.ForUserGranted(Writer, MailFathomPermission.AdminRead));

        // Act
        var result = await editor.PreviewImportAsync(
            new MailRuleSetWrite(Account, ExpectedVersion: 0),
            new MailRuleSetImport(MailRuleSetImportMode.Replace, [Draft("file-invoices")]),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRuleSetChangeOutcome.Previewed, result.Outcome);
    }

    private static MailRuleDraft Draft(string name) => new(
        name,
        "isSeen",
        Actions: [MailRuleAction.Relocate(MailFolderReference.ToAlias(Archive))],
        StopWhenMatched: false,
        Triggers: [MailRuleTrigger.Arrival],
        Schedule: null);

    private static MailRuleDraft Redraft(MailRuleDeclaration rule) => new(
        rule.Name,
        rule.ConditionText,
        rule.Actions,
        rule.StopWhenMatched,
        rule.Triggers,
        rule.Schedule);

    private static MailRuleDeclaration Declared(string name, bool enabled) => new(
        MailRuleIds.For(name),
        name,
        enabled,
        "isSeen",
        Actions: [MailRuleAction.Relocate(MailFolderReference.ToAlias(Archive))],
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
            authorization ?? AccessAuthorizations.ForUserGranted(Writer, MailFathomPermission.AdminRead, MailFathomPermission.AdminConfigurationWrite),
            this.timeProvider);
    }

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
