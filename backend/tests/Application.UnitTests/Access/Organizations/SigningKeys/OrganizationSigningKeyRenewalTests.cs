// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Access.Organizations.SigningKeys;
using MailFathom.Application.Coordination;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Access.Organizations.SigningKeys;

/// <summary>Covers the background pass that mints missing organization signing keys and rotates the ones due.</summary>
public sealed class OrganizationSigningKeyRenewalTests
{
    private static readonly Guid FirstOrganization = new("0199a0c0-0000-7000-8000-0000000000f1");

    private static readonly Guid SecondOrganization = new("0199a0c0-0000-7000-8000-0000000000f2");

    private static readonly Guid MintedKeyId = new("0199a0c0-0000-7000-8000-0000000000a2");

    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Interval = TimeSpan.FromDays(30);

    /// <summary>The interval is measured on the injected clock, so a key minted exactly one interval ago is due and one a moment younger is not.</summary>
    [Fact]
    public async Task RunAsync_AnOrganizationDueAKey_ReplacesItsKeyIfMintedOneIntervalAgoOrEarlierAtTheCurrentInstant()
    {
        // Arrange
        var harness = new RenewalHarness();
        harness.Due(FirstOrganization);
        harness.Answer(FirstOrganization, Replaced());

        // Act
        var pass = await harness.Renewal.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new OrganizationSigningKeyRenewalPass(Renewed: 1, Unminted: 0), pass);
        await harness.Keys.Received(1).FindOrganizationsDueAsync(
            Now - Interval,
            OrganizationSigningKeyRenewal.MaximumOrganizationsPerPass,
            Arg.Any<CancellationToken>());
        await harness.Keys.Received(1).ReplaceActiveKeyAsync(FirstOrganization, Now - Interval, Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_EachOrganization_IsRenewedUnderALeasedScopeOfItsOwn()
    {
        // Arrange
        var harness = new RenewalHarness();
        harness.Due(FirstOrganization, SecondOrganization);
        harness.Answer(FirstOrganization, Replaced());
        harness.Answer(SecondOrganization, Replaced());

        // Act
        await harness.Renewal.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [
                OrganizationSigningKeyRenewal.ScopeFor(FirstOrganization),
                OrganizationSigningKeyRenewal.ScopeFor(SecondOrganization),
            ],
            harness.Leases.Scopes);
        Assert.NotEqual(
            OrganizationSigningKeyRenewal.ScopeFor(FirstOrganization),
            OrganizationSigningKeyRenewal.ScopeFor(SecondOrganization));
    }

    [Fact]
    public async Task RunAsync_ADeploymentWithNoKeyRing_CountsEveryOrganizationItCouldNotGiveAKey()
    {
        // Arrange
        var harness = new RenewalHarness();
        harness.Due(FirstOrganization, SecondOrganization);
        harness.Answer(FirstOrganization, OrganizationSigningKeyReplacement.Of(OrganizationSigningKeyReplacementOutcome.NoKeyRing));
        harness.Answer(SecondOrganization, OrganizationSigningKeyReplacement.Of(OrganizationSigningKeyReplacementOutcome.NoKeyRing));

        // Act
        var pass = await harness.Renewal.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new OrganizationSigningKeyRenewalPass(Renewed: 0, Unminted: 2), pass);
        harness.Telemetry.Received(2).MintRefusedWithoutKeyRing();
    }

    /// <summary>
    /// A second replica minting for the same organization at the same moment loses on the one-active-key index and reads
    /// the winner's key back; it neither counts a renewal it did not make nor reports a missing ring.
    /// </summary>
    [Fact]
    public async Task RunAsync_TheReplicaThatLostTheCreationRace_CountsNeitherARenewalNorAMissingRing()
    {
        // Arrange
        var harness = new RenewalHarness();
        harness.Due(FirstOrganization);
        harness.Answer(FirstOrganization, new OrganizationSigningKeyReplacement(
            OrganizationSigningKeyReplacementOutcome.AlreadyCurrent,
            ActiveKeyId: MintedKeyId,
            RetiredKeyId: null));

        // Act
        var pass = await harness.Renewal.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new OrganizationSigningKeyRenewalPass(Renewed: 0, Unminted: 0), pass);
        harness.Telemetry.DidNotReceive().MintRefusedWithoutKeyRing();
    }

    [Fact]
    public async Task RunAsync_AnotherReplicaHoldsTheOrganizationsLease_LeavesTheRenewalToIt()
    {
        // Arrange
        var harness = new RenewalHarness(new RefusingLeaseRunner());
        harness.Due(FirstOrganization);

        // Act
        var pass = await harness.Renewal.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new OrganizationSigningKeyRenewalPass(Renewed: 0, Unminted: 0), pass);
        await harness.Keys.DidNotReceiveWithAnyArgs().ReplaceActiveKeyAsync(
            default,
            default,
            default,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RunAsync_ACallerRatherThanTheProcess_IsRefusedWithoutReadingTheStore()
    {
        // Arrange
        var harness = new RenewalHarness(
            authorization: AccessAuthorizations.ForAdministratorGranted(MailFathomPermission.AdminCredentialsWrite));

        // Act
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            harness.Renewal.RunAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Empty(harness.Keys.ReceivedCalls());
    }

    private static OrganizationSigningKeyReplacement Replaced() =>
        new(OrganizationSigningKeyReplacementOutcome.Replaced, MintedKeyId, RetiredKeyId: null);

    private sealed class RenewalHarness
    {
        internal RenewalHarness(IWorkLeaseRunner? leases = null, AccessAuthorization? authorization = null)
        {
            this.Keys = Substitute.For<IOrganizationSigningKeyStore>();
            this.Telemetry = Substitute.For<IOrganizationSigningKeyTelemetry>();

            this.Renewal = new OrganizationSigningKeyRenewal(
                authorization ?? AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process),
                this.Keys,
                this.Telemetry,
                leases ?? this.Leases,
                new OrganizationSigningSettings(Interval),
                new FakeTimeProvider(Now));
        }

        internal OrganizationSigningKeyRenewal Renewal { get; }

        internal IOrganizationSigningKeyStore Keys { get; }

        internal IOrganizationSigningKeyTelemetry Telemetry { get; }

        internal RecordingLeaseRunner Leases { get; } = new();

        internal void Due(params Guid[] organizations) =>
            this.Keys.FindOrganizationsDueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(organizations);

        internal void Answer(Guid organization, OrganizationSigningKeyReplacement replacement) =>
            this.Keys.ReplaceActiveKeyAsync(organization, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(replacement);
    }

    /// <summary>Grants every lease, recording the scope each was asked for.</summary>
    private sealed class RecordingLeaseRunner : IWorkLeaseRunner
    {
        internal List<WorkScope> Scopes { get; } = [];

        public async Task<bool> TryRunUnderLeaseAsync(
            WorkScope scope,
            Func<CancellationToken, Task> work,
            CancellationToken cancellationToken)
        {
            this.Scopes.Add(scope);
            await work(cancellationToken);

            return true;
        }
    }

    /// <summary>Stands in for a replica whose every lease another replica already holds.</summary>
    private sealed class RefusingLeaseRunner : IWorkLeaseRunner
    {
        public Task<bool> TryRunUnderLeaseAsync(
            WorkScope scope,
            Func<CancellationToken, Task> work,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }
}
