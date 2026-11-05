// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.Rules.Sets;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules;

/// <summary>Covers the administrator's reading of one account's stored rules.</summary>
public sealed class MailRuleSetReaderTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private readonly InMemoryMailRuleSetStore store = new();

    /// <summary>Disabled rules are part of what an administrator reads, because they are part of what the account holds.</summary>
    [Fact]
    public async Task ReadAsync_ACallerGrantedTheAdministrativeRead_IsServedEveryStoredRuleInOrder()
    {
        // Arrange
        this.store.Arrange(new StoredMailRuleSet(Account, 3, [Declare("first", "isSeen", enabled: false), Declare("second", "isDraft")]));
        var reader = this.ReaderFor(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));

        // Act
        var reading = await reader.ReadAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, reading.Stored.Version);
        Assert.Equal(["first", "second"], reading.Stored.Declarations.Select(rule => rule.Name));
        Assert.False(reading.IsHeld);
    }

    /// <summary>An enabled rule that no longer compiles holds the whole set, and the reading names it by identifier.</summary>
    [Fact]
    public async Task ReadAsync_AnEnabledRuleThatNoLongerCompiles_ReportsTheSetHeld()
    {
        // Arrange
        this.store.Arrange(new StoredMailRuleSet(Account, 1, [Declare("broken", "NoSuchFact == ("), Declare("fine", "isSeen")]));
        var reader = this.ReaderFor(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));

        // Act
        var reading = await reader.ReadAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(reading.IsHeld);
        Assert.Equal([MailRuleIds.For("broken")], reading.UncompiledRules);
    }

    /// <summary>A disabled rule is never run, so one that does not compile holds nothing.</summary>
    [Fact]
    public async Task ReadAsync_ADisabledRuleThatDoesNotCompile_LeavesTheSetRunning()
    {
        // Arrange
        this.store.Arrange(new StoredMailRuleSet(Account, 1, [Declare("broken", "NoSuchFact == (", enabled: false)]));
        var reader = this.ReaderFor(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));

        // Act
        var reading = await reader.ReadAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(reading.IsHeld);
    }

    /// <summary>The grant is the authority here rather than at the transport, so an entrypoint that passed no filter meets the same refusal.</summary>
    [Fact]
    public async Task ReadAsync_ACallerGrantedOnlyTheAuditRead_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var reader = this.ReaderFor(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminAuditRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => reader.ReadAsync(Account, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminRead, refusal.RequiredPermission);
    }

    private static MailRuleDeclaration Declare(string name, string condition, bool enabled = true) => new(
        MailRuleIds.For(name),
        name,
        enabled,
        condition,
        Actions: [],
        StopWhenMatched: false,
        Triggers: [MailRuleTrigger.Arrival],
        Schedule: null);

    private MailRuleSetReader ReaderFor(AccessAuthorization authorization) => new(
        this.store,
        new ScriptedMailRuleConditionCompiler(),
        MailRuleConditionBounds.Default,
        authorization);
}
