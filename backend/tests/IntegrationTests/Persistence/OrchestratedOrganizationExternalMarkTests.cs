// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access.Organizations;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves an organization's External mark is written and read back per mail account as PostgreSQL runs it.</summary>
/// <remarks>
/// <para>
/// The read is a correlated <c>EXISTS</c> over the organizations beside a <c>uuid[]</c> containment, and the write an
/// <c>UPDATE</c> with no load, so a translation that matched an account in no organization, or every account of an
/// organization that never turned the mark on, would publish the chip on mail nobody asked to mark. A substitute
/// translates nothing, which is why the claim is made here.
/// </para>
/// <para>
/// This class shares a database with every other in the collection, so it reads only the accounts and organizations it
/// created for the test alone, and everything it wrote is removed in a <c>finally</c>.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedOrganizationExternalMarkTests(MailFathomOrchestrationFixture orchestration)
{
    private static readonly DateTimeOffset RecordedAt = new(2026, 11, 12, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReadMailAccountsMarkingExternalMailAsync_AfterOneOrganizationTurnedTheMarkOn_AnswersExactlyItsAccounts()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var marking = Guid.CreateVersion7();
        var notMarking = Guid.CreateVersion7();
        var inMarking = Guid.CreateVersion7();
        var inNotMarking = Guid.CreateVersion7();
        var inNoOrganization = Guid.CreateVersion7();
        Guid[] accounts = [inMarking, inNotMarking, inNoOrganization];
        MailAccountId[] asked = [.. accounts.Select(account => MailAccountId.Create(account.ToString()))];

        try
        {
            await CreateOrganizationAsync(services, marking, cancellationToken);
            await CreateOrganizationAsync(services, notMarking, cancellationToken);
            var seeded = await services.CommitAsync(
                async (_, session, token) =>
                {
                    var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, token);

                    context.MailAccountRecords.AddRange(
                        Record(inMarking, marking),
                        Record(inNotMarking, notMarking),
                        Record(inNoOrganization, organizationId: null));

                    await context.SaveChangesAsync(token);
                },
                cancellationToken);
            Assert.Equal(PersistenceCommitResult.Committed, seeded);

            // Act
            var before = await ReadMarkingAsync(services, asked, cancellationToken);
            var written = await SetExternalMarkAsync(services, marking, marksExternalMail: true, cancellationToken);
            var after = await ReadMarkingAsync(services, asked, cancellationToken);

            // Assert
            Assert.Empty(before);
            Assert.Equal(OrganizationWriteOutcome.Written, written);
            Assert.Equal([MailAccountId.Create(inMarking.ToString())], after);
        }
        finally
        {
            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                    .MailAccountRecords
                    .Where(account => accounts.Contains(account.Id))
                    .ExecuteDeleteAsync(token),
                CancellationToken.None);
            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>().DeleteAsync(marking, token),
                CancellationToken.None);
            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>().DeleteAsync(notMarking, token),
                CancellationToken.None);
        }
    }

    [Fact]
    public async Task SetExternalMarkAsync_AnOrganizationThisDeploymentDoesNotHold_IsAnsweredAsUnknown()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        // Act
        var written = await SetExternalMarkAsync(services, Guid.CreateVersion7(), marksExternalMail: true, cancellationToken);

        // Assert
        Assert.Equal(OrganizationWriteOutcome.UnknownOrganization, written);
    }

    private static async Task<MailAccountId[]> ReadMarkingAsync(
        OrchestratedMailFathomServices services,
        IReadOnlyCollection<MailAccountId> asked,
        CancellationToken cancellationToken) =>
        [
            .. await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>()
                    .ReadMailAccountsMarkingExternalMailAsync(asked, token),
                cancellationToken),
        ];

    private static async Task<OrganizationWriteOutcome> SetExternalMarkAsync(
        OrchestratedMailFathomServices services,
        Guid organization,
        bool marksExternalMail,
        CancellationToken cancellationToken) =>
        (await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOrganizationStore>()
                .SetExternalMarkAsync(organization, marksExternalMail, token),
            cancellationToken)).Outcome;

    private static async Task CreateOrganizationAsync(
        OrchestratedMailFathomServices services,
        Guid organization,
        CancellationToken cancellationToken) => Assert.Equal(
            OrganizationWriteOutcome.Written,
            (await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>().CreateAsync(
                    organization,
                    $"Organization {organization:N}",
                    OrganizationShortName.Create($"{organization:N}"),
                    RecordedAt,
                    token),
                cancellationToken)).Outcome);

    private static MailAccountRecordEntity Record(Guid id, Guid? organizationId) => new()
    {
        Id = id,
        DisplayName = $"account-{id:N}",
        Document = "{}",
        OrganizationId = organizationId,
        Version = 1,
        CreatedAt = RecordedAt,
        UpdatedAt = RecordedAt,
    };
}
