// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Access.Organizations;
using MailFathom.Application.Accounts;
using MailFathom.Application.Contacts;
using MailFathom.Application.Persistence;
using MailFathom.Application.StoredFiles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Synchronization;
using MailFathom.Host.Api;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers the administrative routes an organization's contact book is written and read through.</summary>
/// <remarks>
/// The handlers are called directly, as every other administrative route's are: what they decide is whether the
/// caller's scope covers the organization and whether the deployment holds it, and both are answered before the book is
/// reached, so the substituted store is what tells a refusal apart from a write.
/// </remarks>
public sealed class OrganizationContactEndpointsTests
{
    private static readonly Guid Organization = AccessAuthorizations.ScopedOrganization;

    private static readonly Guid AnotherOrganization = new("0198f0aa-0000-7000-8000-00000000f003");

    private static readonly MailAccountId Account = SyntheticMailAccount.Deployment;

    private readonly IContactStore store = Substitute.For<IContactStore>();
    private readonly IContactDirectory directory = Substitute.For<IContactDirectory>();
    private readonly IOrganizationStore organizations = Substitute.For<IOrganizationStore>();
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));

    /// <summary>The organization's book is reached beneath the organization's own route, which <c>mfctl</c> composes from a constant of its own.</summary>
    [Fact]
    public void OrganizationContactRoutes_AreThePathsTheCommandComposes()
    {
        Assert.Equal("/organizations/{organizationId:guid}/contacts", OrganizationContactEndpoints.OrganizationContactsRoute);
    }

    /// <summary>A record written here is an asserted contact of the organization named, for every member to read.</summary>
    [Fact]
    public async Task RecordAsync_AnOrganizationTheDeploymentHolds_WritesItIntoThatOrganizationsBook()
    {
        // Arrange
        this.Holds(Organization);
        this.directory
            .FindHoldersOfAsync(Arg.Any<ContactBookHolder>(), Arg.Any<IReadOnlyCollection<EmailAddress>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<EmailAddress, ContactId>());

        // Act
        var result = await OrganizationContactEndpoints.RecordAsync(
            new DefaultHttpContext(),
            Organization,
            new ContactRecordRequest("Biuro Rachunkowe", ["biuro@example.test"], "biuro@example.test", Note: null),
            this.Book(AdministrativeGrant.WholeSurface),
            this.organizations,
            AdministrativeGrant.WholeSurface,
            TestContext.Current.CancellationToken);

        // Assert
        var written = Assert.IsType<Ok<ContactWriteResponse>>(result.Result);
        Assert.Equal(nameof(ContactOrigin.Asserted), written.Value!.Contact!.Origin);
        Assert.Equal(Organization, written.Value.Contact.Organization);
        await this.store.Received(1).AddAsync(
            Arg.Any<IPersistenceSession>(),
            ContactBookHolder.OfOrganization(Organization),
            Arg.Any<Contact>(),
            Arg.Is<ContactAddressWriter>(writer => writer.Kind == ContactAddressWriterKind.Administrator),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An organization the deployment does not hold and one the caller's scope does not cover are refused alike, before
    /// the book is reached, so the refusal tells an organization's administrator nothing about another organization.
    /// </summary>
    [Fact]
    public async Task RecordAsync_AnOrganizationNotHeldOrNotCovered_RefusesBothAlikeWithoutWriting()
    {
        // Arrange
        this.Holds(Organization);
        var scopedElsewhere = AccessAuthorizations.ForAdministratorScopedAt(
            AssignmentScope.Organization(AnotherOrganization),
            MailFathomPermission.AdminOperate);

        // Act
        var notHeld = await OrganizationContactEndpoints.RecordAsync(
            new DefaultHttpContext(),
            AnotherOrganization,
            new ContactRecordRequest("Biuro Rachunkowe", ["biuro@example.test"], "biuro@example.test", Note: null),
            this.Book(AdministrativeGrant.WholeSurface),
            this.organizations,
            AdministrativeGrant.WholeSurface,
            TestContext.Current.CancellationToken);
        var notCovered = await OrganizationContactEndpoints.RecordAsync(
            new DefaultHttpContext(),
            Organization,
            new ContactRecordRequest("Biuro Rachunkowe", ["biuro@example.test"], "biuro@example.test", Note: null),
            this.Book(scopedElsewhere),
            this.organizations,
            scopedElsewhere,
            TestContext.Current.CancellationToken);

        // Assert
        var first = Assert.IsType<ProblemHttpResult>(notHeld.Result);
        var second = Assert.IsType<ProblemHttpResult>(notCovered.Result);
        Assert.Equal(400, first.StatusCode);
        Assert.Equal(400, second.StatusCode);
        await this.store.DidNotReceiveWithAnyArgs().AddAsync(default!, default!, default!, default!, TestContext.Current.CancellationToken);
    }

    /// <summary>An administrator of another organization is served an empty book rather than told whether this one exists.</summary>
    [Fact]
    public async Task ListAsync_AnAdministratorOfAnotherOrganization_IsServedNothing()
    {
        // Arrange
        var scopedElsewhere = AccessAuthorizations.ForAdministratorScopedAt(
            AssignmentScope.Organization(AnotherOrganization),
            MailFathomPermission.AdminAuditRead);

        // Act
        var result = await OrganizationContactEndpoints.ListAsync(
            Organization,
            pageSize: null,
            cursor: null,
            this.Book(scopedElsewhere),
            scopedElsewhere,
            TestContext.Current.CancellationToken);

        // Assert
        var page = Assert.IsType<Ok<ContactPageResponse>>(result.Result);
        Assert.Empty(page.Value!.Contacts);
        await this.directory.DidNotReceiveWithAnyArgs().ReadPageAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    /// <summary>A record one of the organization's own mailboxes collected is copied into the organization's book, asserted.</summary>
    [Fact]
    public async Task PromoteAsync_ACollectedContactOfOneOfTheOrganizationsMailboxes_CopiesItIntoTheOrganizationsBook()
    {
        // Arrange
        this.Holds(Organization);
        var collected = this.HoldsCollected(Account);
        var placements = new StatedAdministrativeTargets().WithMailAccount(Account, Organization);

        // Act
        var result = await OrganizationContactEndpoints.PromoteAsync(
            new DefaultHttpContext(),
            Organization,
            collected.Id.Value,
            Account.Value,
            this.Book(AdministrativeGrant.WholeSurface),
            this.organizations,
            CatalogServing(Account),
            placements,
            AdministrativeGrant.WholeSurface,
            TestContext.Current.CancellationToken);

        // Assert
        var promoted = Assert.IsType<Ok<ContactWriteResponse>>(result.Result);
        Assert.Equal(nameof(ContactWriteOutcome.Written), promoted.Value!.Outcome);
        await this.store.Received(1).AddAsync(
            Arg.Any<IPersistenceSession>(),
            ContactBookHolder.OfOrganization(Organization),
            Arg.Is<Contact>(copy => copy.Organization == Organization
                && copy.Origin == ContactOrigin.Asserted
                && copy.Id != collected.Id),
            Arg.Is<ContactAddressWriter>(writer => writer.Kind == ContactAddressWriterKind.Administrator),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A mailbox of another organization is refused exactly as one the deployment does not serve, so an administrator
    /// covering two organizations cannot hand one of them a correspondent learned from the other's mail.
    /// </summary>
    [Fact]
    public async Task PromoteAsync_AMailboxOfAnotherOrganization_IsRefusedWithoutWriting()
    {
        // Arrange
        this.Holds(Organization);
        var collected = this.HoldsCollected(Account);
        var placements = new StatedAdministrativeTargets().WithMailAccount(Account, AnotherOrganization);

        // Act
        var result = await OrganizationContactEndpoints.PromoteAsync(
            new DefaultHttpContext(),
            Organization,
            collected.Id.Value,
            Account.Value,
            this.Book(AdministrativeGrant.WholeSurface),
            this.organizations,
            CatalogServing(Account),
            placements,
            AdministrativeGrant.WholeSurface,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<ProblemHttpResult>(result.Result);
        await this.store.DidNotReceiveWithAnyArgs().AddAsync(default!, default!, default!, default!, TestContext.Current.CancellationToken);
    }

    private static IDeploymentMailAccountCatalog CatalogServing(MailAccountId account)
    {
        var catalog = Substitute.For<IDeploymentMailAccountCatalog>();
        IReadOnlyList<ServedMailAccount> served =
        [
            new ServedMailAccount(account, MailAccountDisplayName.Create(account.Value), MailSynchronizationMode.Polling),
        ];

        catalog.ReadServedAccountsAsync(Arg.Any<CancellationToken>()).Returns(served);
        catalog.ReadServedAccountsAsync(Arg.Any<IReadOnlyCollection<MailAccountId>>(), Arg.Any<CancellationToken>())
            .Returns(served);

        return catalog;
    }

    /// <summary>Holds one collected contact in an account's book, and answers that none of its addresses is held elsewhere.</summary>
    private Contact HoldsCollected(MailAccountId account)
    {
        _ = EmailAddress.TryCreate(displayName: null, "anna@example.test", out var address);
        var collected = Contact.Create(
            ContactId.Create(Guid.CreateVersion7(this.clock.GetUtcNow())),
            ContactDisplayName.Create("Anna Kowalska"),
            [address!],
            address!,
            note: null,
            ContactOrigin.Collected,
            this.clock.GetUtcNow(),
            this.clock.GetUtcNow());

        this.directory
            .FindAsync(Arg.Is<ContactBookScope>(scope => scope.Accounts.Contains(account)), collected.Id, Arg.Any<CancellationToken>())
            .Returns(collected);
        this.directory
            .FindHoldersOfAsync(Arg.Any<ContactBookHolder>(), Arg.Any<IReadOnlyCollection<EmailAddress>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<EmailAddress, ContactId>());

        return collected;
    }

    private void Holds(Guid organization) =>
        this.organizations.ExistsAsync(organization, Arg.Any<CancellationToken>()).Returns(true);

    private ContactBook Book(AccessAuthorization authorization)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return new ContactBook(
            this.store,
            this.directory,
            Substitute.For<IStoredFileStore>(),
            new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), this.clock),
            this.clock,
            authorization,
            ClientSignalPublishers.ReachingNobody);
    }

    /// <summary>A session that commits whatever was staged in it.</summary>
    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
