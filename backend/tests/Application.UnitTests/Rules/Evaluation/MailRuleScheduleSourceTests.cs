// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Scheduling;
using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.Application.Rules.Sets;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Synchronization;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules.Evaluation;

/// <summary>Which recurring dispatches the stored rules ask for: one per enabled scheduled rule of a served account.</summary>
public sealed class MailRuleScheduleSourceTests
{
    private static readonly MailAccountId Work = MailAccountId.Create("work");

    private static readonly MailAccountId Personal = MailAccountId.Create("personal");

    private readonly IDeploymentMailAccountCatalog accounts = Substitute.For<IDeploymentMailAccountCatalog>();

    private readonly InMemoryMailRuleSetStore store = new();

    /// <summary>A rule belongs to one account, so its schedule walks that mailbox and no other.</summary>
    [Fact]
    public async Task ReadSchedulesAsync_AScheduledRule_DeclaresOneScheduleForItsOwnAccount()
    {
        // Arrange
        this.ArrangeAccounts(Personal, Work);
        this.store.Arrange(new StoredMailRuleSet(Work, 1, [Scheduled("housekeeping", "Daily at 03:00")]));

        // Act
        var schedules = await this.CreateSource().ReadSchedulesAsync(TestContext.Current.CancellationToken);

        // Assert
        var schedule = Assert.Single(schedules);
        Assert.Equal($"mail-rules:work:{MailRuleIds.For("housekeeping")}", schedule.Id.Value);
        Assert.Equal(Work, schedule.Account);
        Assert.Equal(JobType.RunScheduledMailRules, schedule.Payload.JobType);
    }

    /// <summary>The schedule is named by the rule's identifier, so a rename keeps the dispatch it already had.</summary>
    [Fact]
    public async Task ReadSchedulesAsync_AScheduledRuleRenamed_KeepsItsScheduleIdentity()
    {
        // Arrange
        this.ArrangeAccounts(Work);
        var rule = Scheduled("housekeeping", "Daily at 03:00");
        this.store.Arrange(new StoredMailRuleSet(Work, 1, [rule]));
        var before = await this.CreateSource().ReadSchedulesAsync(TestContext.Current.CancellationToken);
        this.store.Arrange(new StoredMailRuleSet(Work, 2, [rule with { Name = "nightly-tidy" }]));

        // Act
        var after = await this.CreateSource().ReadSchedulesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Assert.Single(before).Id, Assert.Single(after).Id);
    }

    /// <summary>A disabled rule is never run, so its schedule asks for nothing.</summary>
    [Fact]
    public async Task ReadSchedulesAsync_ADisabledScheduledRule_AsksForNoDispatch()
    {
        // Arrange
        this.ArrangeAccounts(Work);
        this.store.Arrange(new StoredMailRuleSet(Work, 1, [Scheduled("housekeeping", "Daily at 03:00") with { Enabled = false }]));

        // Act
        var schedules = await this.CreateSource().ReadSchedulesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(schedules);
    }

    /// <summary>Rules of an account this deployment no longer serves ask for no walk of a mailbox nobody synchronizes.</summary>
    [Fact]
    public async Task ReadSchedulesAsync_AScheduledRuleOfAnAccountNotServed_AsksForNoDispatch()
    {
        // Arrange
        this.ArrangeAccounts(Personal);
        this.store.Arrange(new StoredMailRuleSet(Work, 1, [Scheduled("housekeeping", "Daily at 03:00")]));

        // Act
        var schedules = await this.CreateSource().ReadSchedulesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(schedules);
    }

    /// <summary>Stored rules that declare no schedule have no walk to declare, so the account records are never read for one.</summary>
    [Fact]
    public async Task ReadSchedulesAsync_RulesDeclaringNoSchedule_ReadsNoServedAccount()
    {
        // Arrange
        this.ArrangeAccounts(Work);
        this.store.Arrange(new StoredMailRuleSet(Work, 1, [Declared("on-arrival", [MailRuleTrigger.Arrival], schedule: null)]));

        // Act
        var schedules = await this.CreateSource().ReadSchedulesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(schedules);
        await this.accounts.DidNotReceive().ReadServedAccountsAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>The occasions are the rule's own, so two rules with different intervals declare two schedules.</summary>
    [Fact]
    public async Task ReadSchedulesAsync_TwoScheduledRules_DeclareTheirOwnOccasionsSeparately()
    {
        // Arrange
        this.ArrangeAccounts(Work);
        this.store.Arrange(new StoredMailRuleSet(
            Work,
            1,
            [Scheduled("nightly", "Daily at 03:00"), Scheduled("hourly", "Every 01:00:00")]));

        // Act
        var schedules = await this.CreateSource().ReadSchedulesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            ["daily:03:00:UTC", "every:01:00:00"],
            schedules.Select(schedule => schedule.Recurrence.CanonicalForm));
    }

    private static MailRuleDeclaration Scheduled(string name, string declaration)
    {
        Assert.True(JobRecurrence.TryParse(declaration, out var recurrence, out _));

        return Declared(name, [MailRuleTrigger.Schedule], recurrence);
    }

    private static MailRuleDeclaration Declared(
        string name,
        IReadOnlyList<MailRuleTrigger> triggers,
        JobRecurrence? schedule) => new(
        MailRuleIds.For(name),
        name,
        Enabled: true,
        "isSeen",
        Actions: [],
        StopWhenMatched: false,
        triggers,
        schedule);

    private MailRuleScheduleSource CreateSource() => new(this.store, this.accounts);

    private void ArrangeAccounts(params MailAccountId[] identifiers) => this.accounts.ReadServedAccountsAsync(Arg.Any<CancellationToken>()).Returns(
    [
        .. identifiers.Select(identifier => new ServedMailAccount(
            identifier,
            MailAccountDisplayName.Create(identifier.Value),
            MailSynchronizationMode.Polling)),
    ]);
}
