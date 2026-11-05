// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Actions;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.Rules.Editing;
using MailFathom.Application.Rules.Sets;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Rules;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace MailFathom.IntegrationTests.Idempotency;

/// <summary>Proves that of several writers changing one account's rules over one version, exactly one commits.</summary>
/// <remarks>
/// <para>
/// Both halves are decided by PostgreSQL. A write locks the account's set row before it compares the version it was
/// composed over, so what refuses the second writer is the row lock the first one holds until it commits, after which
/// the second reads the version the first one wrote. A first write to an account has no row to lock, and only a write
/// that commits creates one, so several first writers meet at the set's primary key instead: every one but the first
/// violates it, is retried, and on the retry locks the winner's row and is told the set moved. The account's own
/// <c>mailbox_accounts</c> row, which an account nothing has synchronized yet does not have, is inserted by the same
/// commit and does nothing on a conflict.
/// </para>
/// <para>
/// Each test writes the rules of an account of its own, and the editor is composed in the test from the services the infrastructure registers: the rule
/// registrations are the composition root's, which this harness does not run.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedMailRuleSetWriteTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>How many writers change one set at once: enough that some reliably lose, and few enough to cost seconds.</summary>
    private const int ConcurrentWriters = 6;

    /// <summary>
    /// Writers who all read the same version of a set that already exists meet at its row lock: one commits the next
    /// version and appends one archive row, and every other one is told the set moved rather than writing over it.
    /// </summary>
    [Fact]
    public async Task CreateAsync_ManyWritersComposingOverOneVersion_CommitsOneAndAnswersTheRestWithAVersionConflict()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var account = await HoldAccountAsync(services, cancellationToken);
        var first = await CreateAsync(services, account, "first-rule", expectedVersion: 0, cancellationToken);

        // Act
        var attempts = await ConcurrentIdempotency.RunAsync(
            $"{nameof(MailRuleSetEditor)}.{nameof(MailRuleSetEditor.CreateAsync)}",
            ConcurrentWriters,
            (ordinal, attemptToken) => CreateAsync(
                services,
                account,
                $"concurrent-rule-{ordinal}",
                expectedVersion: first.Version,
                attemptToken),
            cancellationToken);

        // Assert
        Assert.Equal(MailRuleSetChangeOutcome.Committed, first.Outcome);
        Assert.Equal(1, first.Version);
        attempts.AssertSingleEffect(await CountArchivedAsync(services, account, aboveVersion: first.Version, cancellationToken));
        Assert.Empty(attempts.Failures);
        Assert.Equal(
            [MailRuleSetChangeOutcome.Committed, .. Enumerable.Repeat(MailRuleSetChangeOutcome.VersionConflict, ConcurrentWriters - 1)],
            attempts.Results.Select(result => result.Outcome).Order());
        Assert.Equal(2, await ReadVersionAsync(services, account, cancellationToken));
        Assert.Equal(2, await CountRulesAsync(services, account, cancellationToken));
    }

    /// <summary>
    /// Writers who all find an account nothing has synchronized — no account row and no set — meet at the set's key
    /// rather than at a lock: one commits version one, and every other one is retried and told the set moved, with none
    /// of them failing.
    /// </summary>
    [Fact]
    public async Task CreateAsync_ManyFirstWritersToAnAccountNothingSynchronized_CommitsOneAndAnswersTheRestWithAVersionConflict()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var account = MailAccountId.Create($"rules-{Guid.CreateVersion7():N}");

        // Act
        var attempts = await ConcurrentIdempotency.RunAsync(
            $"{nameof(MailRuleSetEditor)}.{nameof(MailRuleSetEditor.CreateAsync)}",
            ConcurrentWriters,
            (ordinal, attemptToken) => CreateAsync(
                services,
                account,
                $"first-rule-{ordinal}",
                expectedVersion: 0,
                attemptToken),
            cancellationToken);

        // Assert
        attempts.AssertSingleEffect(await CountArchivedAsync(services, account, aboveVersion: 0, cancellationToken));
        Assert.Empty(attempts.Failures);
        Assert.Equal(
            [MailRuleSetChangeOutcome.Committed, .. Enumerable.Repeat(MailRuleSetChangeOutcome.VersionConflict, ConcurrentWriters - 1)],
            attempts.Results.Select(result => result.Outcome).Order());
        Assert.Equal(1, await ReadVersionAsync(services, account, cancellationToken));
        Assert.Equal(1, await CountRulesAsync(services, account, cancellationToken));
    }

    /// <summary>A write that does not commit leaves nothing behind, not even the rows a first write would have created.</summary>
    [Fact]
    public async Task CreateAsync_AStaleWriteToAnAccountNothingSynchronized_LeavesNoRow()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var account = MailAccountId.Create($"rules-{Guid.CreateVersion7():N}");

        // Act
        var result = await CreateAsync(services, account, "stale-rule", expectedVersion: 5, cancellationToken);

        // Assert
        Assert.Equal(MailRuleSetChangeOutcome.VersionConflict, result.Outcome);
        Assert.Equal(0, await CountRowsNamingAsync(services, account, cancellationToken));
    }

    private static async Task<MailAccountId> HoldAccountAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken)
    {
        var account = MailAccountId.Create($"rules-{Guid.CreateVersion7():N}");

        await OrchestratedMailboxAccountRows.HoldAsync(services, [account], cancellationToken);

        return account;
    }

    private static Task<MailRuleSetChangeResult> CreateAsync(
        OrchestratedMailFathomServices services,
        MailAccountId account,
        string name,
        long expectedVersion,
        CancellationToken cancellationToken) => services.AsCallerInScopeAsync(
            (scope, token) => ComposeEditor(scope).CreateAsync(
                new MailRuleSetWrite(account, expectedVersion),
                new MailRuleDraft(
                    name,
                    "isSeen",
                    Actions: [],
                    StopWhenMatched: false,
                    [MailRuleTrigger.Arrival],
                    Schedule: null),
                token),
            [MailFathomPermission.AdminConfigurationWrite],
            cancellationToken);

    private static MailRuleSetEditor ComposeEditor(IServiceProvider scope)
    {
        var permissions = Substitute.For<IMailRuleActionPermissionReader>();
        permissions.GetRuleActionPermissions(Arg.Any<MailAccountId>()).Returns(MailRuleActionPermissions.Default);

        return new MailRuleSetEditor(
            scope.GetRequiredService<IMailRuleSetStore>(),
            new MailRuleSetValidator(
                new NCalcMailRuleConditionCompiler(),
                permissions,
                scope.GetRequiredService<MailFolderReferenceResolver>()),
            MailRuleConditionBounds.Default,
            scope.GetRequiredService<OptimisticConcurrencyRetryPolicy>(),
            scope.GetRequiredService<AccessAuthorization>(),
            TimeProvider.System);
    }

    private static Task<int> CountArchivedAsync(
        OrchestratedMailFathomServices services,
        MailAccountId account,
        long aboveVersion,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .MailRuleSetRevisions
                .AsNoTracking()
                .CountAsync(row => row.MailboxAccountId == account.Value && row.Version > aboveVersion, token),
            cancellationToken);

    private static Task<long> ReadVersionAsync(
        OrchestratedMailFathomServices services,
        MailAccountId account,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .MailRuleSets
                .AsNoTracking()
                .Where(set => set.MailboxAccountId == account.Value)
                .Select(set => set.Version)
                .SingleAsync(token),
            cancellationToken);

    private static Task<int> CountRowsNamingAsync(
        OrchestratedMailFathomServices services,
        MailAccountId account,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var dbContext = scope.GetRequiredService<MailFathomDbContext>();

                return await dbContext.MailboxAccounts.AsNoTracking().CountAsync(row => row.Id == account.Value, token)
                    + await dbContext.MailRuleSets.AsNoTracking().CountAsync(set => set.MailboxAccountId == account.Value, token);
            },
            cancellationToken);

    private static Task<int> CountRulesAsync(
        OrchestratedMailFathomServices services,
        MailAccountId account,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .MailRules
                .AsNoTracking()
                .CountAsync(rule => rule.MailboxAccountId == account.Value, token),
            cancellationToken);
}
