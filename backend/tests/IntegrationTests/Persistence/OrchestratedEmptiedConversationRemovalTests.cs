// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Threads;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves against real PostgreSQL that a conversation goes with its last stored row and not before.</summary>
/// <remarks>The batching, the bound, and the skip are decided without a database and stay in the unit suite.</remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedEmptiedConversationRemovalTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "emptied-conversations";

    private static readonly Guid Emptied = Guid.Parse("0197a000-0000-7000-8000-000000003133");
    private static readonly Guid FoldedIntoEmptied = Guid.Parse("0197a000-0000-7000-8000-000000003134");
    private static readonly Guid Held = Guid.Parse("0197a000-0000-7000-8000-000000003135");
    private static readonly Guid FoldedIntoHeld = Guid.Parse("0197a000-0000-7000-8000-000000003136");

    [Fact]
    public async Task RemoveEmptiedAsync_EmptiedAndHeldConversations_RemovesOnlyTheEmptiedOneAndWhatWasFoldedIntoIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await SeedAsync(services, cancellationToken);

        // Act
        var result = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IEmailThreadStore>()
                .RemoveEmptiedAsync(session, SyntheticMailAccount.AccountId, limit: 1000, token),
            cancellationToken);

        // Assert
        Assert.Equal(PersistenceCommitResult.Committed, result);

        await services.InScopeAsync(
            async (scope, token) =>
            {
                var context = scope.GetRequiredService<MailFathomDbContext>();
                Guid[] seeded = [Emptied, FoldedIntoEmptied, Held, FoldedIntoHeld];

                Assert.Equal(
                    [Held, FoldedIntoHeld],
                    await context.EmailThreads
                        .Where(thread => seeded.Contains(thread.Id))
                        .OrderBy(thread => thread.Id == FoldedIntoHeld)
                        .Select(thread => thread.Id)
                        .ToArrayAsync(token));
                Assert.False(await context.EmailThreadStates.AnyAsync(state => state.EmailThreadId == Emptied, token));
                Assert.False(await context.EmailThreadIdentifiers.AnyAsync(binding => binding.EmailThreadId == Emptied, token));
                Assert.True(await context.StoredEmails.AnyAsync(email => email.EmailThreadId == Held, token));

                return true;
            },
            cancellationToken);
    }

    private static async Task SeedAsync(OrchestratedMailFathomServices services, CancellationToken cancellationToken)
    {
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        StoredEmailId stored = default;

        var committed = await services.CommitAsync(
            async (scope, session, token) =>
            {
                var occurrenceId = SyntheticEmail.OccurrenceIn(binding, 9300);

                stored = await scope.GetRequiredService<IEmailMetadataRepository>().UpsertMetadataAsync(
                    session,
                    SyntheticEmail.RemoteMetadataOf(occurrenceId, "emptied-conversations", isRemotelySeen: false),
                    SyntheticEmail.ExtractionOf(occurrenceId, "emptied-conversations", SyntheticEmail.BodyTextContaining("emptied", wordCount: 12)),
                    StoredEmailContentAvailability.Available,
                    token);
            },
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, committed);

        await services.InScopeAsync(
            async (scope, token) =>
            {
                var context = scope.GetRequiredService<MailFathomDbContext>();

                context.EmailThreads.AddRange(
                    Thread(Emptied, mergedInto: null),
                    Thread(FoldedIntoEmptied, Emptied),
                    Thread(Held, mergedInto: null),
                    Thread(FoldedIntoHeld, Held));
                context.EmailThreadIdentifiers.Add(new EmailThreadIdentifierEntity
                {
                    MailboxAccountId = SyntheticMailAccount.AccountId.Value,
                    IdentifierHash = new string('e', EmailThreadIdentifierEntity.IdentifierHashLength),
                    EmailThreadId = Emptied,
                });
                await context.SaveChangesAsync(token);

                await context.Database.ExecuteSqlAsync(
                    $"""INSERT INTO email_thread_states ("EmailThreadId", "Coverage", "DerivedAt", "DerivedFromMessageCount") VALUES ({Emptied}, 'WholeThread', now(), 0)""",
                    token);

                var heldEmail = stored.Value;

                await context.StoredEmails
                    .Where(email => email.Id == heldEmail)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(email => email.EmailThreadId, Held), token);

                return true;
            },
            cancellationToken);
    }

    private static EmailThreadEntity Thread(Guid id, Guid? mergedInto) => new()
    {
        Id = id,
        MailboxAccountId = SyntheticMailAccount.AccountId.Value,
        AssembledAt = SyntheticEmail.ReceivedAt,
        MergedIntoEmailThreadId = mergedInto,
    };
}
