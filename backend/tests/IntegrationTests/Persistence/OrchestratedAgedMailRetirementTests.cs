// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Retention;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves against real PostgreSQL which messages retention reads.</summary>
/// <remarks>What is asked of each, which custody phase runs the pass, and the night's bound are decided without a database and stay in the unit suite.</remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedAgedMailRetirementTests(MailFathomOrchestrationFixture orchestration)
{
    private const int NoBound = 1000;

    private static readonly DateTimeOffset Cutoff = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset BeforeCutoff = Cutoff.AddDays(-1);
    private static readonly DateTimeOffset AfterCutoff = Cutoff.AddDays(1);

    [Fact]
    public async Task ReadAsync_MailOfOneAccount_ReadsOnlyVisibleUnownedMailPastTheAge()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var account = MailAccountId.Create("aged-mail-selection");
        var inbox = await OrchestratedFolderBinding.CommitAsync(services, account, "aged-mail-inbox", "INBOX.aged-mail-inbox", cancellationToken);
        var kept = await OrchestratedFolderBinding.CommitAsync(services, account, "aged-mail-kept", "INBOX.aged-mail-kept", cancellationToken);

        var aged = await StoreAsync(services, account, inbox, 9500, BeforeCutoff, cancellationToken);
        var young = await StoreAsync(services, account, inbox, 9501, AfterCutoff, cancellationToken);
        var excluded = await StoreAsync(services, account, kept, 9502, BeforeCutoff, cancellationToken);
        var tombstoned = await StoreAsync(services, account, inbox, 9503, BeforeCutoff, cancellationToken);
        var beingDeleted = await StoreAsync(services, account, inbox, 9504, BeforeCutoff, cancellationToken);
        var refused = await StoreAsync(services, account, inbox, 9505, BeforeCutoff, cancellationToken);
        var localOnly = await StoreAsync(services, account, inbox, 9506, BeforeCutoff, cancellationToken);
        var ownedByASend = await StoreAsync(services, account, inbox, 9507, BeforeCutoff, cancellationToken);
        var ownedByADraft = await StoreAsync(services, account, inbox, 9508, BeforeCutoff, cancellationToken);

        await ChangeAsync(services, tombstoned, row => row.RemoteExpungeObservedAt = BeforeCutoff, cancellationToken);
        await ChangeAsync(
            services,
            localOnly,
            row =>
            {
                row.RemoteExpungeObservedAt = BeforeCutoff;
                row.IsRetainedAfterRemoteDelete = true;
            },
            cancellationToken);
        await RecordDeleteAsync(services, beingDeleted, MailboxMutationStage.Recorded, MailboxMutationOrigin.Command, cancellationToken);
        await RecordDeleteAsync(services, refused, MailboxMutationStage.Abandoned, MailboxMutationOrigin.Retention, cancellationToken);
        await OwnBySendAsync(services, ownedByASend, cancellationToken);
        await OwnByDraftAsync(services, account, ownedByADraft, cancellationToken);

        // Act
        var read = await services.InScopeAsync(
            async (scope, token) =>
            {
                var factory = scope.GetRequiredService<IPersistenceSessionFactory>();
                await using var session = await factory.BeginSessionAsync(token);

                return await scope.GetRequiredService<IAgedStoredMailReader>().ReadAsync(
                    session,
                    account,
                    new MailMessageRetention(IsEnabled: true, TimeSpan.FromDays(30), ["aged-mail-kept"]),
                    Cutoff,
                    resumeAfter: null,
                    NoBound,
                    token);
            },
            cancellationToken);

        // Assert
        Assert.Equal(new HashSet<StoredEmailId> { aged, localOnly }, read.Select(mail => mail.Email).ToHashSet());
        Assert.DoesNotContain(young, read.Select(mail => mail.Email));
        Assert.DoesNotContain(excluded, read.Select(mail => mail.Email));
        Assert.All(read, mail => Assert.NotNull(mail.Occurrence));
    }

    private static async Task<StoredEmailId> StoreAsync(
        OrchestratedMailFathomServices services,
        MailAccountId account,
        MailFolderResolution binding,
        uint uid,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken)
    {
        StoredEmailId stored = default;

        var committed = await services.CommitAsync(
            async (scope, session, token) =>
            {
                var occurrenceId = SyntheticEmail.OccurrenceIn(account, binding, uid);
                var subject = $"aged-mail-{uid}";

                stored = await scope.GetRequiredService<IEmailMetadataRepository>().UpsertMetadataAsync(
                    session,
                    SyntheticEmail.RemoteMetadataOf(occurrenceId, subject, isRemotelySeen: false),
                    SyntheticEmail.ExtractionOf(occurrenceId, subject, SyntheticEmail.BodyTextContaining("aged", wordCount: 12)),
                    StoredEmailContentAvailability.Available,
                    token);
            },
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, committed);
        await ChangeAsync(services, stored, row => row.ReceivedAt = receivedAt, cancellationToken);

        return stored;
    }

    private static Task<bool> ChangeAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId email,
        Action<StoredEmailEntity> change,
        CancellationToken cancellationToken) => services.InScopeAsync(
        async (scope, token) =>
        {
            var context = scope.GetRequiredService<MailFathomDbContext>();
            change(await context.StoredEmails.SingleAsync(row => row.Id == email.Value, token));
            await context.SaveChangesAsync(token);

            return true;
        },
        cancellationToken);

    private static Task<bool> RecordDeleteAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId email,
        MailboxMutationStage stage,
        MailboxMutationOrigin origin,
        CancellationToken cancellationToken) => services.InScopeAsync(
        async (scope, token) =>
        {
            var context = scope.GetRequiredService<MailFathomDbContext>();
            var stored = await context.StoredEmails
                .Include(row => row.MailFolderBinding)
                .SingleAsync(row => row.Id == email.Value, token);

            context.MailboxMutations.Add(new MailboxMutationEntity
            {
                Id = Guid.CreateVersion7(),
                StoredEmailId = stored.Id,
                StoredEmail = stored,
                MailboxAccountId = stored.MailboxAccountId,
                MailFolderBinding = stored.MailFolderBinding,
                UidValidity = stored.UidValidity!.Value,
                Uid = stored.Uid!.Value,
                Mutation = MailboxMutation.Delete.Name,
                RequesterOrigin = origin,
                RequesterIdentity = "aged-mail",
                Stage = stage,
                RecordedAt = BeforeCutoff,
                StageChangedAt = BeforeCutoff,
            });
            await context.SaveChangesAsync(token);

            return true;
        },
        cancellationToken);

    private static Task<bool> OwnBySendAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId email,
        CancellationToken cancellationToken) => services.InScopeAsync(
        async (scope, token) =>
        {
            var context = scope.GetRequiredService<MailFathomDbContext>();
            var stored = await context.StoredEmails.SingleAsync(row => row.Id == email.Value, token);
            var send = new OutgoingEmailEntity
            {
                Id = Guid.CreateVersion7(),
                UserId = Guid.CreateVersion7(),
                MailboxAccountId = stored.MailboxAccountId,
                RequesterIdentity = "aged-mail",
                Stage = OutgoingEmailStage.Recorded,
                RecordedAt = BeforeCutoff,
                StageChangedAt = BeforeCutoff,
                AvailableAt = BeforeCutoff,
            };
            context.OutgoingEmails.Add(send);
            stored.FiledFromOutgoingEmailId = send.Id;
            await context.SaveChangesAsync(token);

            return true;
        },
        cancellationToken);

    private static Task<bool> OwnByDraftAsync(
        OrchestratedMailFathomServices services,
        MailAccountId account,
        StoredEmailId email,
        CancellationToken cancellationToken) => services.InScopeAsync(
        async (scope, token) =>
        {
            var context = scope.GetRequiredService<MailFathomDbContext>();
            context.MailDrafts.Add(new MailDraftEntity
            {
                Id = Guid.CreateVersion7(),
                UserId = Guid.CreateVersion7(),
                MailboxAccountId = account.Value,
                RequesterIdentity = "aged-mail",
                Subject = string.Empty,
                ComposedAt = BeforeCutoff,
                RevisedAt = BeforeCutoff,
                FiledStoredEmailId = email.Value,
            });
            await context.SaveChangesAsync(token);

            return true;
        },
        cancellationToken);
}
