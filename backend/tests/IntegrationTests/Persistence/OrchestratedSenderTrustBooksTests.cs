// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access.Organizations;
using MailFathom.Application.Contacts;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Emails.Signatures;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Contacts;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the read of the books a stored verdict is combined with reaches the books ADR 0043 names, and no others.</summary>
/// <remarks>
/// Which holdings recognize a sender is unit tested; what only PostgreSQL can show is that the read finds them at all —
/// the account's organization through its stored record, the users it is assigned to through the assignment rows, each
/// book by the holder key its contacts were written under, and the addresses by the comparison form the index is on. A
/// wrong key or a missed assignment would make every contact recognize nobody, which no unit test would notice.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedSenderTrustBooksTests(MailFathomOrchestrationFixture orchestration)
{
    private static readonly DateTimeOffset RecordedAt = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly ContactAddressWriter PersonWriting = new(ContactAddressWriterKind.Person, User: null, Credential: null);

    /// <summary>An organization contact and an assigned user's contact each recognize their sender, and an unassigned user's does not.</summary>
    [Fact]
    public async Task RecognizeAsync_ContactsInTheBooksAnAccountReachesAndOneItDoesNot_RecognizesOnlyThroughTheReachedBooks()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var organization = Guid.CreateVersion7();
        var accountId = Guid.CreateVersion7();
        var unassignedUser = Guid.CreateVersion7();
        var domain = $"{accountId:N}.trust-books.test";
        var organizationContact = ContactOf($"colleague@{domain}");
        var personalContact = ContactOf($"friend@{domain}");
        var unassignedContact = ContactOf($"stranger@{domain}");
        await CreateOrganizationAsync(services, organization, cancellationToken);
        await RecordAccountAsync(services, accountId, organization, cancellationToken);
        Assert.Equal(PersistenceCommitResult.Committed, await OrchestratedForeignUser.ProvisionAsync(services, unassignedUser, cancellationToken));

        try
        {
            await AddAsync(services, ContactBookHolder.OfOrganization(organization), organizationContact, cancellationToken);
            await AddAsync(services, ContactBookHolder.Of(SyntheticMailAccount.User), personalContact, cancellationToken);
            await AddAsync(services, ContactBookHolder.Of(UserId.Create(unassignedUser)), unassignedContact, cancellationToken);
            var account = accountId.ToString("D");

            // Act
            var verdicts = await services.InScopeAsync(
                (scope, token) => SenderTrustBooks.RecognizeAsync(
                    scope.GetRequiredService<MailFathomDbContext>(),
                    [
                        AuthenticatedRow(account, organizationContact, domain),
                        AuthenticatedRow(account, personalContact, domain),
                        AuthenticatedRow(account, unassignedContact, domain),
                    ],
                    token),
                cancellationToken);

            // Assert
            (SenderTrustSource GrantedBy, ContactId? Contact)[] expected =
            [
                (SenderTrustSource.OrganizationContact, organizationContact.Id),
                (SenderTrustSource.PersonalContact, personalContact.Id),
                (SenderTrustSource.None, null),
            ];
            Assert.Equal(expected, verdicts.Select(static verdict => (verdict.GrantedBy, verdict.RecognizingContactId)));
        }
        finally
        {
            await services.InScopeAsync(
                async (scope, token) =>
                {
                    var context = scope.GetRequiredService<MailFathomDbContext>();

                    return await context.Contacts.Where(contact => contact.Id == personalContact.Id.Value).ExecuteDeleteAsync(token)
                        + await context.MailAccountAssignments.Where(assignment => assignment.MailAccountId == accountId).ExecuteDeleteAsync(token)
                        + await context.MailAccountRecords.Where(record => record.Id == accountId).ExecuteDeleteAsync(token);
                },
                CancellationToken.None);
            await OrchestratedForeignUser.EraseAsync(services, unassignedUser);
            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>().DeleteAsync(organization, token),
                CancellationToken.None);
        }
    }

    private static StoredSenderTrustRow AuthenticatedRow(string account, Contact contact, string domain) => new(
        account,
        contact.PreferredAddress.NormalizedAddress,
        SenderTrustLevel.Unknown,
        SenderTrustSource.None,
        TrustedAuthorEvidence.None,
        PolicyRevision: null,
        OrganizationId: null,
        IsStrict: false,
        AuthorAuthenticationOutcome.Authenticated,
        AuthorAuthenticationRoute.DmarcPass,
        domain.ToUpperInvariant(),
        DmarcPolicyReading.NotEstablished,
        SignatureCheckResult.Skipped,
        SignatureUnverifiableReason: null,
        SignatureCheckResult.Skipped,
        SignatureCheckResult.Skipped,
        SignatureCheckResult.Skipped,
        SignatureCheckResult.Skipped);

    private static Contact ContactOf(string address)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, address, out var emailAddress));

        return Contact.Create(
            ContactId.Create(Guid.CreateVersion7(RecordedAt)),
            ContactDisplayName.Create(address),
            [emailAddress],
            emailAddress,
            note: null,
            ContactOrigin.Asserted,
            RecordedAt,
            RecordedAt);
    }

    private static async Task AddAsync(
        OrchestratedMailFathomServices services,
        ContactBookHolder holder,
        Contact contact,
        CancellationToken cancellationToken) => Assert.Equal(
        PersistenceCommitResult.Committed,
        await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IContactStore>().AddAsync(session, holder, contact, PersonWriting, token),
            cancellationToken));

    private static async Task RecordAccountAsync(
        OrchestratedMailFathomServices services,
        Guid accountId,
        Guid organization,
        CancellationToken cancellationToken) => Assert.Equal(
        PersistenceCommitResult.Committed,
        await services.CommitAsync(
            async (_, session, token) =>
            {
                var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, token);
                context.MailAccountRecords.Add(new MailAccountRecordEntity
                {
                    Id = accountId,
                    DisplayName = "Trust books account",
                    OrganizationId = organization,
                    Document = "{}",
                    Version = 1,
                    CreatedAt = RecordedAt,
                    UpdatedAt = RecordedAt,
                });
                context.MailAccountAssignments.Add(new MailAccountAssignmentEntity
                {
                    UserId = SyntheticMailAccount.User.Value,
                    MailAccountId = accountId,
                    AssignedAt = RecordedAt,
                });
                await context.SaveChangesAsync(token);
            },
            cancellationToken));

    private static Task<OrganizationWriteResult> CreateOrganizationAsync(
        OrchestratedMailFathomServices services,
        Guid organization,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOrganizationStore>().CreateAsync(
                organization,
                $"Organization {organization:N}",
                OrganizationShortName.Create($"{organization:N}"),
                RecordedAt,
                token),
            cancellationToken);
}
