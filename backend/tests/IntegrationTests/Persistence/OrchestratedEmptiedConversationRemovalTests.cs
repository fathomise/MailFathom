// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Threads;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves against real PostgreSQL that a conversation goes with its last stored row and not before.</summary>
/// <remarks>
/// Every case runs on accounts of its own, because the database is shared with the rest of the collection and a removal
/// on the served account would reach any conversation another class left empty. The batching, the night's bound, and
/// the skip are decided without a database and stay in the unit suite.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedEmptiedConversationRemovalTests(MailFathomOrchestrationFixture orchestration)
{
    private const int NoBound = 1000;

    private static readonly MailAccountId Removing = MailAccountId.Create("emptied-conversations-removing");
    private static readonly MailAccountId Neighbour = MailAccountId.Create("emptied-conversations-neighbour");
    private static readonly MailAccountId Bounded = MailAccountId.Create("emptied-conversations-bounded");

    private static readonly Guid Emptied = Guid.Parse("0197a000-0000-7000-8000-000000003133");
    private static readonly Guid FoldedIntoEmptied = Guid.Parse("0197a000-0000-7000-8000-000000003134");
    private static readonly Guid Held = Guid.Parse("0197a000-0000-7000-8000-000000003135");
    private static readonly Guid FoldedIntoHeld = Guid.Parse("0197a000-0000-7000-8000-000000003136");
    private static readonly Guid TombstonesOnly = Guid.Parse("0197a000-0000-7000-8000-000000003137");
    private static readonly Guid EmptiedOfTheNeighbour = Guid.Parse("0197a000-0000-7000-8000-000000003138");
    private static readonly Guid FirstBounded = Guid.Parse("0197a000-0000-7000-8000-000000003139");
    private static readonly Guid SecondBounded = Guid.Parse("0197a000-0000-7000-8000-000000003140");

    [Fact]
    public async Task RemoveEmptiedAsync_ConversationsOfOneAccount_RemovesOnlyTheEmptiedOneAndWhatWasFoldedIntoIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await SeedRemovingAccountAsync(services, cancellationToken);
        await SeedEmptiedAccountAsync(services, Neighbour, [EmptiedOfTheNeighbour], cancellationToken);

        // Act
        await RemoveAsync(services, Removing, NoBound, cancellationToken);

        // Assert
        await services.InScopeAsync(
            async (scope, token) =>
            {
                var context = scope.GetRequiredService<MailFathomDbContext>();
                Guid[] seeded = [Emptied, FoldedIntoEmptied, Held, FoldedIntoHeld, TombstonesOnly, EmptiedOfTheNeighbour];

                Assert.Equal(
                    new[] { Held, FoldedIntoHeld, TombstonesOnly, EmptiedOfTheNeighbour }.Order(),
                    await context.EmailThreads
                        .Where(thread => seeded.Contains(thread.Id))
                        .OrderBy(thread => thread.Id)
                        .Select(thread => thread.Id)
                        .ToArrayAsync(token));
                Assert.False(await context.EmailThreadStates.AnyAsync(state => state.EmailThreadId == Emptied, token));
                Assert.False(await context.EmailThreadIdentifiers.AnyAsync(binding => binding.EmailThreadId == Emptied, token));
                Assert.True(await context.StoredEmails.AnyAsync(email => email.EmailThreadId == Held, token));

                return true;
            },
            cancellationToken);
    }

    [Fact]
    public async Task RemoveEmptiedAsync_MoreEmptiedConversationsThanTheLimit_RemovesTheLimitAndLeavesTheRest()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await SeedEmptiedAccountAsync(services, Bounded, [FirstBounded, SecondBounded], cancellationToken);

        // Act
        var removed = await RemoveAsync(services, Bounded, limit: 1, cancellationToken);

        // Assert
        Assert.Equal(1, removed);

        await services.InScopeAsync(
            async (scope, token) =>
            {
                Assert.Equal(
                    1,
                    await scope.GetRequiredService<MailFathomDbContext>().EmailThreads
                        .CountAsync(thread => thread.MailboxAccountId == Bounded.Value, token));

                return true;
            },
            cancellationToken);
    }

    private static async Task<int> RemoveAsync(
        OrchestratedMailFathomServices services,
        MailAccountId account,
        int limit,
        CancellationToken cancellationToken)
    {
        var removed = 0;

        var result = await services.CommitAsync(
            async (scope, session, token) => removed = await scope.GetRequiredService<IEmailThreadStore>()
                .RemoveEmptiedAsync(session, account, limit, token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, result);

        return removed;
    }

    private static Task<bool> SeedEmptiedAccountAsync(
        OrchestratedMailFathomServices services,
        MailAccountId account,
        Guid[] threads,
        CancellationToken cancellationToken) => services.InScopeAsync(
        async (scope, token) =>
        {
            var context = scope.GetRequiredService<MailFathomDbContext>();

            context.MailboxAccounts.Add(new MailboxAccountEntity { Id = account.Value });
            context.EmailThreads.AddRange(threads.Select(thread => Thread(thread, account, mergedInto: null)));
            await context.SaveChangesAsync(token);

            return true;
        },
        cancellationToken);

    private static async Task SeedRemovingAccountAsync(OrchestratedMailFathomServices services, CancellationToken cancellationToken)
    {
        var binding = await OrchestratedFolderBinding.CommitAsync(
            services,
            Removing,
            "emptied-conversations",
            "INBOX.emptied-conversations",
            cancellationToken);
        var heldEmail = await StoreAsync(services, binding, 9300, cancellationToken);
        var tombstone = await StoreAsync(services, binding, 9301, cancellationToken);

        await services.InScopeAsync(
            async (scope, token) =>
            {
                var context = scope.GetRequiredService<MailFathomDbContext>();

                context.EmailThreads.AddRange(
                    Thread(Emptied, Removing, mergedInto: null),
                    Thread(FoldedIntoEmptied, Removing, Emptied),
                    Thread(Held, Removing, mergedInto: null),
                    Thread(FoldedIntoHeld, Removing, Held),
                    Thread(TombstonesOnly, Removing, mergedInto: null));
                context.EmailThreadIdentifiers.Add(new EmailThreadIdentifierEntity
                {
                    MailboxAccountId = Removing.Value,
                    IdentifierHash = new string('e', EmailThreadIdentifierEntity.IdentifierHashLength),
                    EmailThreadId = Emptied,
                });
                await context.SaveChangesAsync(token);

                await context.Database.ExecuteSqlAsync(
                    $"""INSERT INTO email_thread_states ("EmailThreadId", "Coverage", "DerivedAt", "DerivedFromMessageCount") VALUES ({Emptied}, 'WholeThread', now(), 0)""",
                    token);

                await context.StoredEmails
                    .Where(email => email.Id == heldEmail.Value)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(email => email.EmailThreadId, Held), token);
                await context.StoredEmails
                    .Where(email => email.Id == tombstone.Value)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(email => email.EmailThreadId, TombstonesOnly)
                            .SetProperty(email => email.RemoteExpungeObservedAt, SyntheticEmail.ReceivedAt),
                        token);

                return true;
            },
            cancellationToken);
    }

    private static async Task<StoredEmailId> StoreAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        uint uid,
        CancellationToken cancellationToken)
    {
        StoredEmailId stored = default;

        var committed = await services.CommitAsync(
            async (scope, session, token) =>
            {
                var occurrenceId = SyntheticEmail.OccurrenceIn(Removing, binding, uid);
                var subject = $"emptied-conversations-{uid}";

                stored = await scope.GetRequiredService<IEmailMetadataRepository>().UpsertMetadataAsync(
                    session,
                    SyntheticEmail.RemoteMetadataOf(occurrenceId, subject, isRemotelySeen: false),
                    SyntheticEmail.ExtractionOf(occurrenceId, subject, SyntheticEmail.BodyTextContaining("emptied", wordCount: 12)),
                    StoredEmailContentAvailability.Available,
                    token);
            },
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, committed);

        return stored;
    }

    private static EmailThreadEntity Thread(Guid id, MailAccountId account, Guid? mergedInto) => new()
    {
        Id = id,
        MailboxAccountId = account.Value,
        AssembledAt = SyntheticEmail.ReceivedAt,
        MergedIntoEmailThreadId = mergedInto,
    };
}
