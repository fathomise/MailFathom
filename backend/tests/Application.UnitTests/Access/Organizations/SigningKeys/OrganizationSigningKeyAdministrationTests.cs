// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Access.Organizations.SigningKeys;
using MailFathom.Application.Paging;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Access.Organizations.SigningKeys;

/// <summary>Covers which grant admits each act on an organization's signing keys, and which acts are audited.</summary>
public sealed class OrganizationSigningKeyAdministrationTests
{
    private static readonly Guid OrganizationId = AccessAuthorizations.ScopedOrganization;

    private static readonly Guid OtherOrganizationId = new("0197c0de-0000-4000-8000-000000000004");

    private static readonly Guid ActiveKeyId = new("0199a0c0-0000-7000-8000-0000000000a1");

    private static readonly Guid MintedKeyId = new("0199a0c0-0000-7000-8000-0000000000a2");

    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    private static readonly AdministrativeListingQuery FirstPage = AdministrativeListingQuery.Create(pageSize: null, after: null)!;

    [Fact]
    public async Task ReadAsync_ACallerReadingAtTheOrganizationsScope_ReadsItsKeys()
    {
        // Arrange
        var harness = new AdministrationHarness(AccessAuthorizations.ForAdministratorScopedAt(
            AssignmentScope.Organization(OrganizationId),
            MailFathomPermission.AdminRead));
        var listing = new OrganizationSigningKeyListing(
            OrganizationId,
            "Test Firma",
            [new OrganizationSigningKey(ActiveKeyId, Now.AddDays(-3), RetiredAt: null, RevokedAt: null)],
            ContinuesAfter: null);
        harness.Keys.ReadAsync(OrganizationId, FirstPage, Arg.Any<CancellationToken>()).Returns(listing);

        // Act
        var read = await harness.Administration.ReadAsync(OrganizationId, FirstPage, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(listing, read);
    }

    /// <summary>An organization the caller's scope does not cover is answered as absent, the way one that does not exist is.</summary>
    [Fact]
    public async Task ReadAsync_AnOrganizationOutsideTheCallersScope_IsAnsweredAsAbsentWithoutReadingTheStore()
    {
        // Arrange
        var harness = new AdministrationHarness(AccessAuthorizations.ForAdministratorScopedAt(
            AssignmentScope.Organization(OrganizationId),
            MailFathomPermission.AdminRead));

        // Act
        var read = await harness.Administration.ReadAsync(OtherOrganizationId, FirstPage, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(read);
        Assert.Empty(harness.Keys.ReceivedCalls());
    }

    [Fact]
    public async Task RotateAsync_ACallerGrantedOnlyTheRead_IsRefusedWithoutTouchingTheStore()
    {
        // Arrange
        var harness = new AdministrationHarness(AccessAuthorizations.ForAdministratorGranted(MailFathomPermission.AdminRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            harness.Administration.RotateAsync(OrganizationId, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminCredentialsWrite, refusal.RequiredPermission);
        Assert.Empty(harness.Keys.ReceivedCalls());
        Assert.Empty(harness.Auditor.ReceivedCalls());
    }

    /// <summary>A revocation changes what every receiving account trusts, so an organization's own administrator does not reach it.</summary>
    [Fact]
    public async Task RevokeAsync_ACallerHoldingTheCredentialsWriteOnlyAtTheOrganization_IsRefusedWithoutTouchingTheStore()
    {
        // Arrange
        var harness = new AdministrationHarness(AccessAuthorizations.ForAdministratorScopedAt(
            AssignmentScope.Organization(OrganizationId),
            MailFathomPermission.AdminCredentialsWrite));

        // Act, Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            harness.Administration.RevokeAsync(OrganizationId, ActiveKeyId, TestContext.Current.CancellationToken));
        Assert.Empty(harness.Keys.ReceivedCalls());
    }

    /// <summary>A rotation by hand replaces whatever is active, however young, so the cut-off is the instant itself.</summary>
    [Fact]
    public async Task RotateAsync_ACallerGrantedTheCredentialsWrite_ReplacesTheActiveKeyNowAndAuditsTheRotation()
    {
        // Arrange
        var harness = new AdministrationHarness(AccessAuthorizations.ForAdministratorGranted(MailFathomPermission.AdminCredentialsWrite));
        harness.Keys.ReplaceActiveKeyAsync(OrganizationId, Now, Now, Arg.Any<CancellationToken>())
            .Returns(new OrganizationSigningKeyReplacement(
                OrganizationSigningKeyReplacementOutcome.Replaced,
                MintedKeyId,
                ActiveKeyId));

        // Act
        var rotation = await harness.Administration.RotateAsync(OrganizationId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OrganizationSigningKeyReplacementOutcome.Replaced, rotation.Outcome);
        await harness.Auditor.Received(1).RecordAsync(
            new OrganizationSigningKeyChange(
                OrganizationSigningKeyAct.Rotated,
                OrganizationId,
                ActiveKeyId,
                MintedKeyId,
                "test-administrator",
                Now),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RotateAsync_ADeploymentWithNoKeyRing_ReportsItAndAuditsNothing()
    {
        // Arrange
        var harness = new AdministrationHarness(AccessAuthorizations.ForAdministratorGranted(MailFathomPermission.AdminCredentialsWrite));
        harness.Keys.ReplaceActiveKeyAsync(OrganizationId, Now, Now, Arg.Any<CancellationToken>())
            .Returns(OrganizationSigningKeyReplacement.Of(OrganizationSigningKeyReplacementOutcome.NoKeyRing));

        // Act
        var rotation = await harness.Administration.RotateAsync(OrganizationId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OrganizationSigningKeyReplacementOutcome.NoKeyRing, rotation.Outcome);
        Assert.Empty(harness.Auditor.ReceivedCalls());
    }

    [Fact]
    public async Task RevokeAsync_TheActiveKey_AuditsTheRevocationNamingTheReplacementMintedWithIt()
    {
        // Arrange
        var harness = new AdministrationHarness(AccessAuthorizations.ForAdministratorGranted(MailFathomPermission.AdminCredentialsWrite));
        harness.Keys.RevokeAsync(OrganizationId, ActiveKeyId, Now, Arg.Any<CancellationToken>())
            .Returns(new OrganizationSigningKeyRevocation(
                OrganizationSigningKeyRevocationOutcome.Revoked,
                WasActive: true,
                MintedKeyId));

        // Act
        var revocation = await harness.Administration.RevokeAsync(
            OrganizationId,
            ActiveKeyId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MintedKeyId, revocation.ReplacementKeyId);
        await harness.Auditor.Received(1).RecordAsync(
            new OrganizationSigningKeyChange(
                OrganizationSigningKeyAct.Revoked,
                OrganizationId,
                ActiveKeyId,
                MintedKeyId,
                "test-administrator",
                Now),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeAsync_AKeyAlreadyRevoked_ChangesNothingAnAuditorWouldLookFor()
    {
        // Arrange
        var harness = new AdministrationHarness(AccessAuthorizations.ForAdministratorGranted(MailFathomPermission.AdminCredentialsWrite));
        harness.Keys.RevokeAsync(OrganizationId, ActiveKeyId, Now, Arg.Any<CancellationToken>())
            .Returns(OrganizationSigningKeyRevocation.Of(OrganizationSigningKeyRevocationOutcome.AlreadyRevoked));

        // Act
        var revocation = await harness.Administration.RevokeAsync(
            OrganizationId,
            ActiveKeyId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OrganizationSigningKeyRevocationOutcome.AlreadyRevoked, revocation.Outcome);
        Assert.Empty(harness.Auditor.ReceivedCalls());
    }

    private sealed class AdministrationHarness
    {
        internal AdministrationHarness(AccessAuthorization authorization)
        {
            this.Keys = Substitute.For<IOrganizationSigningKeyStore>();
            this.Auditor = Substitute.For<IOrganizationSigningKeyAuditor>();

            this.Administration = new OrganizationSigningKeyAdministration(
                authorization,
                this.Keys,
                this.Auditor,
                new FakeTimeProvider(Now));
        }

        internal OrganizationSigningKeyAdministration Administration { get; }

        internal IOrganizationSigningKeyStore Keys { get; }

        internal IOrganizationSigningKeyAuditor Auditor { get; }
    }
}
