// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Actions;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.Rules.Sets;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules.Sets;

/// <summary>Covers how an account's stored rules become the set an evaluation pass runs, and when that set is compiled again.</summary>
public sealed class StoredMailRuleSetSourceTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private readonly InMemoryMailRuleSetStore store = new();
    private readonly ScriptedMailRuleConditionCompiler compiler = new();
    private readonly MailRuleSetCompilationCache cache = new();

    [Fact]
    public async Task ReadAsync_AStoredSet_CompilesItsEnabledRulesInPositionOrder()
    {
        // Arrange
        var fileIt = MailRuleAction.Relocate(MailFolderReference.ToAlias(MailFolderAlias.Create("archive")));
        var stored = new StoredMailRuleSet(
            Account,
            3,
            [
                Declared("file-invoices", "isSeen") with { Actions = [fileIt], StopWhenMatched = true },
                Declared("paused", "isSeen") with { Enabled = false },
                Declared("mark-newsletters", "isSeen"),
            ]);
        this.store.Arrange(stored);

        // Act
        var ruleSet = await this.CreateSource().ReadAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(ruleSet.IsHeld);
        Assert.Equal(stored.Revision, ruleSet.Revision);
        Assert.Equal([MailRuleIds.For("file-invoices"), MailRuleIds.For("mark-newsletters")], ruleSet.Rules.Select(rule => rule.Id));
        Assert.Equal(["file-invoices", "mark-newsletters"], ruleSet.Rules.Select(rule => rule.Name));
        Assert.Equal([fileIt], ruleSet.Rules[0].Actions.Actions);
        Assert.True(ruleSet.Rules[0].StopWhenMatched);
    }

    [Fact]
    public async Task ReadAsync_AnAccountNobodyWroteARuleFor_AnswersAnEmptySetThatRuns()
    {
        // Act
        var ruleSet = await this.CreateSource().ReadAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(ruleSet.Rules);
        Assert.False(ruleSet.IsHeld);
        Assert.Equal(MailRuleSetRevision.Create([]), ruleSet.Revision);
    }

    /// <summary>Running the rules that still compile would apply a set nobody wrote, so one broken enabled rule holds them all.</summary>
    [Fact]
    public async Task ReadAsync_AnEnabledRuleThatNoLongerCompiles_HoldsTheSetNamingThatRule()
    {
        // Arrange
        this.store.Arrange(new StoredMailRuleSet(
            Account,
            2,
            [Declared("broken", $"{ScriptedMailRuleConditionCompiler.UnknownFact} == 1"), Declared("fine", "isSeen")]));

        // Act
        var ruleSet = await this.CreateSource().ReadAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(ruleSet.IsHeld);
        Assert.Equal([MailRuleIds.For("broken")], ruleSet.UncompiledRules);
    }

    /// <summary>A disabled rule never runs, so a condition of its that no longer compiles holds nothing.</summary>
    [Fact]
    public async Task ReadAsync_ADisabledRuleThatNoLongerCompiles_LeavesTheSetRunning()
    {
        // Arrange
        this.store.Arrange(new StoredMailRuleSet(
            Account,
            2,
            [
                Declared("broken", $"{ScriptedMailRuleConditionCompiler.UnknownFact} == 1") with { Enabled = false },
                Declared("fine", "isSeen"),
            ]));

        // Act
        var ruleSet = await this.CreateSource().ReadAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(ruleSet.IsHeld);
        Assert.Equal([MailRuleIds.For("fine")], ruleSet.Rules.Select(rule => rule.Id));
    }

    /// <summary>A source lives for one scope and the cache for the process, so a revision is compiled once however many passes read it.</summary>
    [Fact]
    public async Task ReadAsync_AnUnchangedRevisionReadFromAnotherScope_IsServedWithoutCompilingAgain()
    {
        // Arrange
        this.store.Arrange(new StoredMailRuleSet(Account, 1, [Declared("file-invoices", "isSeen")]));
        var first = await this.CreateSource().ReadAsync(Account, TestContext.Current.CancellationToken);
        var compilations = this.compiler.CompileCount;

        // Act
        var second = await this.CreateSource().ReadAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(first, second);
        Assert.Equal(compilations, this.compiler.CompileCount);
    }

    /// <summary>A committed write changes the revision, so the next pass runs the set as it now stands rather than the remembered one.</summary>
    [Fact]
    public async Task ReadAsync_ARevisionAWriteChanged_CompilesTheNewSet()
    {
        // Arrange
        var rule = Declared("file-invoices", "isSeen");
        this.store.Arrange(new StoredMailRuleSet(Account, 1, [rule]));
        await this.CreateSource().ReadAsync(Account, TestContext.Current.CancellationToken);
        var changed = new StoredMailRuleSet(Account, 2, [rule with { ConditionText = "false" }]);
        this.store.Arrange(changed);
        var compilations = this.compiler.CompileCount;

        // Act
        var ruleSet = await this.CreateSource().ReadAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(changed.Revision, ruleSet.Revision);
        Assert.Equal(compilations + 1, this.compiler.CompileCount);
    }

    /// <summary>
    /// A version repeats once an account is deleted and created again under the same identifier, so a remembered set of
    /// the old account is never run against the new one merely because the two reached the same version.
    /// </summary>
    [Fact]
    public async Task ReadAsync_TheSameVersionOfARecreatedAccount_CompilesTheNewSet()
    {
        // Arrange
        this.store.Arrange(new StoredMailRuleSet(Account, 2, [Declared("delete-everything", "isSeen")]));
        await this.CreateSource().ReadAsync(Account, TestContext.Current.CancellationToken);
        var recreated = new StoredMailRuleSet(Account, 2, [Declared("file-invoices", "isSeen")]);
        this.store.Arrange(recreated);

        // Act
        var ruleSet = await this.CreateSource().ReadAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(recreated.Revision, ruleSet.Revision);
        Assert.Equal([MailRuleIds.For("file-invoices")], ruleSet.Rules.Select(rule => rule.Id));
    }

    /// <summary>A set compiled under other bounds may hold a condition these bounds refuse, so it is never reused across them.</summary>
    [Fact]
    public async Task ReadAsync_TheSameRevisionUnderOtherBounds_IsCompiledAgain()
    {
        // Arrange
        this.store.Arrange(new StoredMailRuleSet(Account, 1, [Declared("file-invoices", "isSeen")]));
        await this.CreateSource().ReadAsync(Account, TestContext.Current.CancellationToken);
        var narrower = MailRuleConditionBounds.Create(maxLength: 3, maxNestingDepth: 16, TimeSpan.FromSeconds(1));

        // Act
        var ruleSet = await this.CreateSource(narrower).ReadAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(ruleSet.IsHeld);
        Assert.Equal([MailRuleIds.For("file-invoices")], ruleSet.UncompiledRules);
    }

    /// <summary>The cache is bounded by account, and reaching the bound forgets what it remembered rather than growing past it.</summary>
    [Fact]
    public async Task ReadAsync_MoreAccountsThanTheCacheRemembers_CompilesTheFirstAccountAgain()
    {
        // Arrange
        MailAccountId[] accounts =
        [
            .. Enumerable.Range(0, MailRuleSetCompilationCache.MaximumCachedAccounts + 1)
                .Select(index => MailAccountId.Create($"account-{index}")),
        ];
        var source = this.CreateSource();

        foreach (var account in accounts)
        {
            this.store.Arrange(new StoredMailRuleSet(account, 1, [Declared("file-invoices", "isSeen")]));
            await source.ReadAsync(account, TestContext.Current.CancellationToken);
        }

        var compilations = this.compiler.CompileCount;

        // Act
        await source.ReadAsync(accounts[0], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(compilations + 1, this.compiler.CompileCount);
    }

    private static MailRuleDeclaration Declared(string name, string condition) => new(
        MailRuleIds.For(name),
        name,
        Enabled: true,
        condition,
        Actions: [],
        StopWhenMatched: false,
        [MailRuleTrigger.Arrival],
        Schedule: null);

    private StoredMailRuleSetSource CreateSource(MailRuleConditionBounds? bounds = null) => new(
        this.store,
        this.compiler,
        bounds ?? MailRuleConditionBounds.Default,
        this.cache);
}
