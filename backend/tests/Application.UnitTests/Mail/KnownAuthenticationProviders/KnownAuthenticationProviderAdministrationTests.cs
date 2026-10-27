// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Mail.KnownAuthenticationProviders;
using MailFathom.Application.Paging;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.KnownAuthenticationProviders;

/// <summary>Covers which grant admits each act on the known authentication providers, and who a change is recorded against.</summary>
public sealed class KnownAuthenticationProviderAdministrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static readonly AdministrativeListingQuery FirstPage = AdministrativeListingQuery.Create(pageSize: null, after: null)!;

    private static readonly KnownAuthenticationProvider Google = new("imap.gmail.com", "mx.google.com");

    [Fact]
    public async Task AddAsync_ADeploymentAdministrator_AddsTheEntryUnderTheirIdentityAndTheClock()
    {
        // Arrange
        var harness = new AdministrationHarness(AccessAuthorizations.ForAdministratorGranted(MailFathomPermission.AdminConfigurationWrite));
        harness.Providers.AddAsync(Google, "test-administrator", Now, Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var added = await harness.Administration.AddAsync(Google, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(added);
        await harness.Providers.Received(1).AddAsync(Google, "test-administrator", Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveAsync_ADeploymentAdministrator_RemovesTheHostUnderTheirIdentity()
    {
        // Arrange
        var harness = new AdministrationHarness(AccessAuthorizations.ForAdministratorGranted(MailFathomPermission.AdminConfigurationWrite));
        harness.Providers.RemoveAsync("imap.gmail.com", "test-administrator", Now, Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var removed = await harness.Administration.RemoveAsync("imap.gmail.com", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(removed);
    }

    /// <summary>An entry decides what every account on a host believes across every organization, so no organization's administrator reaches it.</summary>
    [Fact]
    public async Task AddAsync_ACallerHoldingTheWriteOnlyAtAnOrganization_IsRefusedWithoutTouchingTheStore()
    {
        // Arrange
        var harness = new AdministrationHarness(AccessAuthorizations.ForAdministratorScopedAt(
            AssignmentScope.Organization(AccessAuthorizations.ScopedOrganization),
            MailFathomPermission.AdminConfigurationWrite));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            harness.Administration.AddAsync(Google, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminConfigurationWrite, refusal.RequiredPermission);
        Assert.Empty(harness.Providers.ReceivedCalls());
    }

    [Fact]
    public async Task RemoveAsync_ACallerGrantedOnlyTheRead_IsRefusedWithoutTouchingTheStore()
    {
        // Arrange
        var harness = new AdministrationHarness(AccessAuthorizations.ForAdministratorGranted(MailFathomPermission.AdminRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            harness.Administration.RemoveAsync("imap.gmail.com", TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminConfigurationWrite, refusal.RequiredPermission);
        Assert.Empty(harness.Providers.ReceivedCalls());
    }

    [Fact]
    public async Task ReadAsync_ACallerReadingOnlyAtAnOrganization_IsRefusedWithoutTouchingTheStore()
    {
        // Arrange
        var harness = new AdministrationHarness(AccessAuthorizations.ForAdministratorScopedAt(
            AssignmentScope.Organization(AccessAuthorizations.ScopedOrganization),
            MailFathomPermission.AdminRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            harness.Administration.ReadAsync(FirstPage, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminRead, refusal.RequiredPermission);
        Assert.Empty(harness.Providers.ReceivedCalls());
    }

    [Fact]
    public async Task ReadAsync_ADeploymentReader_ReadsThePage()
    {
        // Arrange
        var harness = new AdministrationHarness(AccessAuthorizations.ForAdministratorGranted(MailFathomPermission.AdminRead));
        var listing = new KnownAuthenticationProviderListing(
            [new ListedKnownAuthenticationProvider(Guid.CreateVersion7(Now), "imap.gmail.com", "mx.google.com", "migration", Now)],
            ContinuesAfter: null);
        harness.Providers.ReadAsync(FirstPage, Arg.Any<CancellationToken>()).Returns(listing);

        // Act
        var read = await harness.Administration.ReadAsync(FirstPage, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(listing, read);
    }

    private sealed class AdministrationHarness
    {
        internal AdministrationHarness(AccessAuthorization authorization) =>
            this.Administration = new KnownAuthenticationProviderAdministration(authorization, this.Providers, new FakeTimeProvider(Now));

        internal IKnownAuthenticationProviderStore Providers { get; } = Substitute.For<IKnownAuthenticationProviderStore>();

        internal KnownAuthenticationProviderAdministration Administration { get; }
    }
}
