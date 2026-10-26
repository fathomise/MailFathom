// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text;
using MailFathom.Application.Access.Organizations;
using MailFathom.Application.Access.Organizations.SigningKeys;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the statements an organization signature is verified against answer what ADR 0042 says, and nothing more.</summary>
/// <remarks>
/// The verifier's own rules are unit tested against a double that applies the same filters; what only PostgreSQL can
/// show is that the queries do apply them — a retired key still answers, a revoked one and another organization's do
/// not, and a blind copy is found by the <c>Message-ID</c> a send was opened with and only through the account it was
/// sent through.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedOrganizationSenderDirectoryTests(MailFathomOrchestrationFixture orchestration)
{
    private static readonly DateTimeOffset RecordedAt = new(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);

    /// <summary>A retired key keeps verifying, and a revoked key and another organization's key answer nothing.</summary>
    [Fact]
    public async Task FindVerifyingKeyAsync_RetiredRevokedAndForeignKeys_AnswersOnlyTheRetiredOne()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var organization = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        await CreateOrganizationAsync(services, organization, cancellationToken);
        await CreateOrganizationAsync(services, other, cancellationToken);

        try
        {
            var retired = await ActiveKeyAsync(services, organization, cancellationToken);
            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationSigningKeyStore>()
                    .ReplaceActiveKeyAsync(organization, RecordedAt.AddDays(1), RecordedAt.AddDays(1), token),
                cancellationToken);
            var revoked = await ActiveKeyAsync(services, organization, cancellationToken);
            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationSigningKeyStore>()
                    .RevokeAsync(organization, revoked, RecordedAt.AddDays(2), token),
                cancellationToken);
            var foreign = await ActiveKeyAsync(services, other, cancellationToken);

            // Act
            var answers = await services.InScopeAsync(
                async (scope, token) =>
                {
                    var directory = new PersistedOrganizationSenderDirectory(new Lazy<MailFathomDbContext>(scope.GetRequiredService<MailFathomDbContext>()));

                    return (
                        Retired: await directory.FindVerifyingKeyAsync(organization, retired, token),
                        Revoked: await directory.FindVerifyingKeyAsync(organization, revoked, token),
                        Foreign: await directory.FindVerifyingKeyAsync(organization, foreign, token));
                },
                cancellationToken);

            // Assert
            Assert.Equal(32, answers.Retired?.Length);
            Assert.Null(answers.Revoked);
            Assert.Null(answers.Foreign);
        }
        finally
        {
            await DeleteOrganizationAsync(services, organization);
            await DeleteOrganizationAsync(services, other);
        }
    }

    /// <summary>A send opened with a <c>Message-ID</c> names its blind copies to that identifier and its own account alone.</summary>
    [Fact]
    public async Task ListBlindCopyRecipientsAsync_SendOpenedWithAMessageId_NamesItsBlindCopiesThroughItsOwnAccountOnly()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var organization = Guid.CreateVersion7();
        var accountId = Guid.CreateVersion7();
        var account = MailAccountId.Create(accountId.ToString("D"));
        var messageId = $"{accountId:N}@org.example.test";
        await CreateOrganizationAsync(services, organization, cancellationToken);
        await RecordAccountAsync(services, accountId, organization, cancellationToken);

        try
        {
            await OpenSendAsync(services, account, messageId, cancellationToken);

            // Act
            var answers = await services.InScopeAsync(
                async (scope, token) =>
                {
                    var directory = new PersistedOrganizationSenderDirectory(new Lazy<MailFathomDbContext>(scope.GetRequiredService<MailFathomDbContext>()));

                    return (
                        Organization: await directory.FindOrganizationAsync(account, token),
                        Accounts: await directory.ListAccountsAsync(organization, token),
                        Through: await directory.ListBlindCopyRecipientsAsync([account], messageId, token),
                        Elsewhere: await directory.ListBlindCopyRecipientsAsync([MailAccountId.Create("elsewhere")], messageId, token));
                },
                cancellationToken);

            // Assert
            Assert.Equal(organization, answers.Organization);
            Assert.Equal([account], answers.Accounts);
            Assert.Equal(["BOB@ORG.EXAMPLE.TEST"], answers.Through.Select(static address => address.NormalizedAddress.ToUpperInvariant()));
            Assert.Empty(answers.Elsewhere);
        }
        finally
        {
            await services.InScopeAsync(
                async (scope, token) =>
                {
                    var context = scope.GetRequiredService<MailFathomDbContext>();

                    return await context.OutgoingEmails.Where(email => email.MailboxAccountId == account.Value).ExecuteDeleteAsync(token)
                        + await context.MailAccountRecords.Where(record => record.Id == accountId).ExecuteDeleteAsync(token);
                },
                CancellationToken.None);
            await DeleteOrganizationAsync(services, organization);
        }
    }

    private static async Task OpenSendAsync(
        OrchestratedMailFathomServices services,
        MailAccountId account,
        string messageId,
        CancellationToken cancellationToken)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, "carol@org.example.test", out var to));
        Assert.True(EmailAddress.TryCreate(displayName: null, "bob@org.example.test", out var blind));
        var request = OutgoingEmailRequest.Create(
            account,
            SyntheticMailAccount.User,
            OutgoingEmailRequester.Command($"blind-copy-{messageId}"),
            [OutgoingRecipient.Create(to, OutgoingRecipientRole.To), OutgoingRecipient.Create(blind, OutgoingRecipientRole.Bcc)]);
        var mime = Encoding.ASCII.GetBytes($"Message-ID: <{messageId}>\r\nSubject: s\r\n\r\nBody.\r\n");

        Assert.Equal(
            PersistenceCommitResult.Committed,
            await services.CommitAsync(
                (scope, session, token) => scope.GetRequiredService<IOutgoingEmailStore>()
                    .OpenAsync(session, request, OutgoingEmailPrincipal.Of("organization-directory"), mime, token),
                cancellationToken));
    }

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
                    DisplayName = "Organization member",
                    OrganizationId = organization,
                    Document = "{}",
                    Version = 1,
                    CreatedAt = RecordedAt,
                    UpdatedAt = RecordedAt,
                });
                await context.SaveChangesAsync(token);
            },
            cancellationToken));

    private static Task<Guid> ActiveKeyAsync(
        OrchestratedMailFathomServices services,
        Guid organization,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().OrganizationSigningKeys
                .Where(key => key.OrganizationId == organization && key.RetiredAt == null && key.RevokedAt == null)
                .Select(key => key.Id)
                .SingleAsync(token),
            cancellationToken);

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

    private static Task<OrganizationWriteResult> DeleteOrganizationAsync(
        OrchestratedMailFathomServices services,
        Guid organization) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOrganizationStore>().DeleteAsync(organization, token),
            CancellationToken.None);
}
