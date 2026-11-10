// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using MailFathom.Application.Access;
using MailFathom.Application.Paging;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Decisions;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.Configuration.UserSettings.Administration;
using MailFathom.Host.Signals;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings.Administration;

/// <summary>
/// Covers what an administrator does to the roster itself: who this deployment holds, who joins it, and who leaves.
/// The rules worth stating are the ones that stop a deployment from becoming one that serves the wrong person — the
/// several-user refusal and the organization a user joins — and the one act that disposes of everything recorded for
/// somebody. A label is not among them: two users may carry one, so nothing here refuses a label another user carries.
/// </summary>
public sealed class UserRosterAdministrationTests
{
    private const string AdministratorIdentity = "operations";

    private static readonly AdministrativeListingQuery FirstPage = AdministrativeListingQuery.Create(pageSize: null, after: null)!;

    [Fact]
    public async Task ReadRosterAsync_ADeploymentHoldingUsers_ReportsEachOneWithWhatThisProcessIsDoingAboutThem()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminRead);
        harness.Holding(
            new UserRecord(SyntheticUser.Deployment, "alex"),
            new UserRecord(SyntheticUser.Another, "morgan"));
        harness.Serving(SyntheticUser.Deployment);

        // Act
        var roster = await harness.Roster.ReadRosterAsync(FirstPage, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [("alex", true), ("morgan", false)],
            roster.Entries.Select(entry => (entry.DisplayName, entry.Served)));
    }

    /// <summary>A page the directory says more follow is answered with where the next one continues, so a walk reaches every user.</summary>
    [Fact]
    public async Task ReadRosterAsync_APageWithMoreFollowing_CarriesWhereTheNextPageContinues()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminRead);
        var query = AdministrativeListingQuery.Create(pageSize: 1, after: SyntheticUser.Another.Value)!;
        harness.Directory.ReadUserPageAsync(query, Arg.Any<IReadOnlySet<AssignmentScope>>(), Arg.Any<CancellationToken>())
            .Returns(new AdministrativeListingPage<UserRecord>(
                [new UserRecord(SyntheticUser.Deployment, "alex")],
                ContinuesAfter: SyntheticUser.Deployment.Value));

        // Act
        var roster = await harness.Roster.ReadRosterAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("alex", Assert.Single(roster.Entries).DisplayName);
        Assert.Equal(SyntheticUser.Deployment.Value, roster.ContinuesAfter);
    }

    /// <summary>Rows that could not be read list their users as unserved rather than failing the whole page.</summary>
    [Fact]
    public async Task ReadRosterAsync_RowsThatCannotBeRead_ListTheirUsersAsUnserved()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminRead);
        harness.Holding(new UserRecord(SyntheticUser.Another, "morgan"));
        harness.UserRows.ReadVersionsAsync(Arg.Any<IReadOnlyCollection<UserId>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new UserSettingsUnreadableException("The user records could not be read."));

        // Act
        var roster = await harness.Roster.ReadRosterAsync(FirstPage, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(Assert.Single(roster.Entries).Served);
    }

    /// <summary>
    /// Whether a listed user is served is answered from what this replica holds and one reading of the page's rows, so a
    /// page of a thousand people costs one statement rather than a read and a secret resolution for each of them.
    /// </summary>
    [Fact]
    public async Task ReadRosterAsync_UsersThisReplicaHasNotComposed_AreListedFromTheirRowsWithoutReadingTheirRecords()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminRead);
        harness.Holding(
            new UserRecord(SyntheticUser.Deployment, "alex"),
            new UserRecord(SyntheticUser.Another, "morgan"));
        harness.UserRows.ReadVersionsAsync(Arg.Any<IReadOnlyCollection<UserId>>(), Arg.Any<CancellationToken>())
            .Returns([new UserSettingsDocumentVersion(SyntheticUser.Another, 1)]);

        // Act
        var roster = await harness.Roster.ReadRosterAsync(FirstPage, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [("alex", false), ("morgan", true)],
            roster.Entries.Select(entry => (entry.DisplayName, entry.Served)));
        await harness.UserRows.DidNotReceive().ReadAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadRosterAsync_ACallerHoldingNoAdministrativeRead_IsRefused()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.MailRead);

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Roster.ReadRosterAsync(FirstPage, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminRead, refusal.RequiredPermission);
    }

    /// <summary>
    /// The identifier is minted here rather than supplied, and it is a version 7 value like every identifier MailFathom
    /// mints — ADR 0036 records the rule and the residual it accepts for an identifier naming a person.
    /// </summary>
    [Fact]
    public async Task ProvisionAsync_ALabelTheDeploymentAccepts_MintsAVersionSevenIdentifier()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);

        // Act
        var outcome = await harness.Roster.ProvisionAsync("alex", organizationId: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.IsProvisioned);
        Assert.Equal(7, outcome.User.Value.Version);
    }

    /// <summary>
    /// The record rather than only the envelope, because a user nothing declares is served from their own record or
    /// from nothing at all — and the marker beside the document is what the next start reads to decide that. It
    /// declares nothing beyond the language, because everything else a record can state is the user's own to state
    /// later and a mailbox is a record of its own; what matters is that the document exists and that a save of it would
    /// be accepted, which a record stating no language would not be.
    /// </summary>
    [Fact]
    public async Task ProvisionAsync_ALabelTheDeploymentAccepts_CommitsARecordNamingTheirLanguageBesideTheEnvelope()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);

        // Act
        var outcome = await harness.Roster.ProvisionAsync("alex", organizationId: null, TestContext.Current.CancellationToken);

        // Assert
        await harness.Documents.Received(1).CommitAsync(
            outcome.User,
            """{"Language":"English"}""",
            UserEndpointAccess.Everywhere,
            1,
            Arg.Any<CancellationToken>());
        Assert.Equal("alex", harness.ServedUsers.Peek(outcome.User)?.DisplayName);
    }

    /// <summary>
    /// A recorded user is announced so every other replica counts its users again at once, and only once this replica
    /// has counted them itself, so the replica that recorded the user is never the one still answering for the old count.
    /// </summary>
    [Fact]
    public async Task ProvisionAsync_ALabelTheDeploymentAccepts_CountsTheUsersAgainBeforeAnnouncingTheChange()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        var heard = await RosterAnnouncementListener.ListenAsync(harness.Backplane, harness.CountsRead);

        // Act
        await harness.Roster.ProvisionAsync("alex", organizationId: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([1], heard);
    }

    /// <summary>A refused provisioning recorded nobody, so no replica is asked to read anything again.</summary>
    [Fact]
    public async Task ProvisionAsync_IntoAnOrganizationTheInsertFindsNoLonger_AnnouncesNothing()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.OrganizationGone();
        var heard = await RosterAnnouncementListener.ListenAsync(harness.Backplane, () => true);

        // Act
        await harness.Roster.ProvisionAsync(
            "alex",
            AccessAuthorizations.ScopedOrganization,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(heard);
    }

    /// <summary>
    /// An erasure is announced so every other replica stops serving the user at once, and only once this replica has
    /// let go of them and counted its users again.
    /// </summary>
    [Fact]
    public async Task EraseAsync_AUserThisDeploymentHolds_LetsGoOfThemAndCountsAgainBeforeAnnouncingTheChange()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminErase);
        harness.Serving(SyntheticUser.Deployment);
        harness.Erasing(SyntheticUser.Deployment);
        var heard = await RosterAnnouncementListener.ListenAsync(
            harness.Backplane,
            () => (Held: harness.ServedUsers.Peek(SyntheticUser.Deployment) is not null, Counted: harness.CountsRead()));

        // Act
        await harness.Roster.EraseAsync(SyntheticUser.Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([(false, 1)], heard);
    }

    /// <summary>Erasing a user the deployment does not hold removed nothing, so no replica is asked to read anything again.</summary>
    [Fact]
    public async Task EraseAsync_AUserThisDeploymentDoesNotHold_AnnouncesNothing()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminErase);
        var heard = await RosterAnnouncementListener.ListenAsync(harness.Backplane, () => true);

        // Act
        await harness.Roster.EraseAsync(SyntheticUser.Another, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(heard);
    }

    /// <summary>
    /// Two people may carry one name, so a label another user already carries records the user beside them, and the
    /// label written is the one asked for, trimmed. Nothing here reads the roster for it: a refusal would tell an
    /// administrator scoped to one organization that somebody outside it carries the label.
    /// </summary>
    [Fact]
    public async Task ProvisionAsync_ALabelAnotherUserAlreadyCarries_RecordsTheUserUnderIt()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(new UserRecord(SyntheticUser.Another, "alex"));

        // Act
        var outcome = await harness.Roster.ProvisionAsync("  alex  ", organizationId: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.IsProvisioned);
        Assert.NotEqual(SyntheticUser.Another, outcome.User);
        await harness.Provisioning.Received(1).ProvisionAsync(outcome.User, "alex", null, Arg.Any<CancellationToken>());
    }

    /// <summary>An insert that found its organization gone recorded nobody, so no record is written for an identifier nothing holds and nothing is counted again.</summary>
    [Fact]
    public async Task ProvisionAsync_IntoAnOrganizationTheInsertFindsNoLonger_WritesNoRecordAndCountsNothing()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.OrganizationGone();

        // Act
        var outcome = await harness.Roster.ProvisionAsync(
            "alex",
            AccessAuthorizations.ScopedOrganization,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.IsProvisioned);
        await harness.Documents.DidNotReceiveWithAnyArgs()
            .CommitAsync(default, default!, default, default, TestContext.Current.CancellationToken);
        Assert.Equal(0, harness.CountsRead());
    }

    /// <summary>
    /// A user is provisioned the record the policies governing them leave it to state: nothing where a policy
    /// supplies the language a record must otherwise name, and the row carries the switches those policies resolve.
    /// </summary>
    [Theory]
    [InlineData("""{"Users":{"Defaults":{"Language":"Polish"}}}""", "{}", true)]
    [InlineData("""{"Users":{"Forced":{"EndpointAccess":{"McpEndpoint":false}}}}""", """{"Language":"English"}""", false)]
    public async Task ProvisionAsync_AUserGovernedByAPolicy_IsProvisionedWhatThePolicyLeavesTheirRecordToState(
        string deploymentPolicy,
        string provisioned,
        bool servedOnTheMcpEndpoint)
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Writer.Otherwise = StatedPolicies.Governing(deployment: deploymentPolicy);

        // Act
        var outcome = await harness.Roster.ProvisionAsync("alex", organizationId: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.IsProvisioned);
        await harness.Documents.Received(1).CommitAsync(
            outcome.User,
            provisioned,
            new UserEndpointAccess(servedOnTheMcpEndpoint, ClientEndpoint: true),
            1,
            Arg.Any<CancellationToken>());
    }

    /// <summary>A record is never composed under part of what governs it, so a policy this build does not read refuses the provisioning and says what to do with the label it left.</summary>
    [Fact]
    public async Task ProvisionAsync_AUserGovernedByAPolicyThisBuildDoesNotRead_IsRefusedNamingThePolicy()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Writer.Otherwise = StatedPolicies.Governing(deployment: """{"Users":{"Forced":{"Dialect":"Silesian"}}}""");

        // Act
        var outcome = await harness.Roster.ProvisionAsync("alex", organizationId: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.IsProvisioned);
        Assert.StartsWith("The settings policy of the deployment", outcome.RefusalMessage, StringComparison.Ordinal);
        await harness.Documents.DidNotReceiveWithAnyArgs()
            .CommitAsync(default, default!, default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The envelope and the record are two writes, so the user can be removed between them — and an outcome reporting
    /// the provisioning as done would leave an administrator believing this deployment holds somebody it holds no
    /// record for, which is the one state every read of that user then answers as an absence.
    /// </summary>
    [Fact]
    public async Task ProvisionAsync_AUserRemovedBeforeTheirRecordWasWritten_IsRefusedRatherThanReportedAsProvisioned()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Documents
            .CommitAsync(
                Arg.Any<UserId>(),
                Arg.Any<string>(),
                Arg.Any<UserEndpointAccess>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>())
            .Returns((long?)null);

        // Act
        var outcome = await harness.Roster.ProvisionAsync("alex", organizationId: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.IsProvisioned);
        Assert.NotNull(outcome.RefusalMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ProvisionAsync_NoLabelAtAll_IsRefusedWithoutReadingTheRoster(string? displayName)
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);

        // Act
        var outcome = await harness.Roster.ProvisionAsync(displayName, organizationId: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.IsProvisioned);
        await harness.Directory.DidNotReceiveWithAnyArgs()
            .ReadUsersAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>The rules the column and the declared collection are held to, asked here so a label refused in a file is refused over a route.</summary>
    [Fact]
    public async Task ProvisionAsync_ALabelPastWhatTheColumnStores_IsRefusedNamingTheBound()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);

        // Act
        var outcome = await harness.Roster.ProvisionAsync(
            new string('a', UserRecord.MaximumDisplayNameLength + 1),
            organizationId: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.IsProvisioned);
        Assert.Contains(
            UserRecord.MaximumDisplayNameLength.ToString(CultureInfo.InvariantCulture),
            outcome.RefusalMessage!,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A second user beside a user-facing surface that admits a caller naming nobody would leave that surface
    /// serving one person another person's mail, so the roster is held to one user instead.
    /// </summary>
    [Fact]
    public async Task ProvisionAsync_ASecondUserWhileAUserFacingSurfaceAdmitsACallerNamingNobody_IsRefused()
    {
        // Arrange
        var harness = new RosterHarness(
            MailFathomPermission.AdminConfigurationWrite,
            clientEndpoint: new ClientEndpointOptions { Enabled = true });
        harness.Holding(new UserRecord(SyntheticUser.Deployment, "alex"));
        harness.UserRows.ReadVersionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new UserSettingsDocumentVersion(SyntheticUser.Deployment, 1)]);

        // Act
        var outcome = await harness.Roster.ProvisionAsync("morgan", organizationId: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.IsProvisioned);
        Assert.Contains("requires no authentication", outcome.RefusalMessage!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The refusal is about a second user rather than about the surface, so the first user of a deployment serving an
    /// unauthenticated surface is still recorded — which is the deployment an easy first run produces.
    /// </summary>
    [Fact]
    public async Task ProvisionAsync_TheFirstUserWhileAUserFacingSurfaceAdmitsACallerNamingNobody_IsRecorded()
    {
        // Arrange
        var harness = new RosterHarness(
            MailFathomPermission.AdminConfigurationWrite,
            clientEndpoint: new ClientEndpointOptions { Enabled = true });

        // Act
        var outcome = await harness.Roster.ProvisionAsync("alex", organizationId: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.IsProvisioned);
    }

    /// <summary>
    /// An administrator acts for the deployment rather than for a person, which is what makes recording a second user
    /// reachable at all — and, with no surface to refuse it for, the users already held are not even read.
    /// </summary>
    [Fact]
    public async Task ProvisionAsync_ASecondUserWhileOnlyTheAdministrativeSurfaceIsServed_IsRecordedWithoutReadingTheUsersHeld()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(new UserRecord(SyntheticUser.Deployment, "alex"));

        // Act
        var outcome = await harness.Roster.ProvisionAsync("morgan", organizationId: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.IsProvisioned);
        await harness.Directory.DidNotReceiveWithAnyArgs().ReadUsersAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>A caller holding the write nowhere is refused whether or not the request names an organization.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProvisionAsync_ACallerHoldingOnlyTheAdministrativeRead_IsRefusedWithoutRecordingAnybody(bool intoAnOrganization)
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminRead);
        Guid? organizationId = intoAnOrganization ? AccessAuthorizations.ScopedOrganization : null;

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Roster.ProvisionAsync("alex", organizationId, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminConfigurationWrite, refusal.RequiredPermission);
        Assert.Empty(harness.Provisioning.ReceivedCalls());
    }

    public static TheoryData<AssignmentScope> ScopesCoveringTheUser => [.. AccessAuthorizations.ScopesCoveringTheirTarget];

    public static TheoryData<AssignmentScope> ScopesOutsideTheUser => [.. AccessAuthorizations.ScopesOutsideTheirTarget];

    public static TheoryData<AssignmentScope> ScopesCoveringTheOrganization =>
        [AssignmentScope.Deployment, AssignmentScope.Organization(AccessAuthorizations.ScopedOrganization)];

    /// <summary>Gets the narrower scopes that reach no organization: another organization, a member of this one, and somebody else.</summary>
    public static TheoryData<AssignmentScope> ScopesOutsideTheOrganization =>
        [.. AccessAuthorizations.ScopesOutsideTheirTarget, AssignmentScope.User(AccessAuthorizations.ScopedHolder)];

    public static TheoryData<AssignmentScope> ScopesBelowTheDeployment =>
    [
        AssignmentScope.Organization(AccessAuthorizations.ScopedOrganization),
        AssignmentScope.User(AccessAuthorizations.ScopedHolder),
    ];

    /// <summary>A listing never refuses over a scope: it answers within the one the caller reads at, whichever kind it is.</summary>
    [Theory]
    [MemberData(nameof(ScopesCoveringTheUser))]
    public async Task ReadRosterAsync_ACallerReadingAtOneScope_AsksTheDirectoryWithinThatScopeAlone(AssignmentScope scope)
    {
        // Arrange
        var harness = HarnessScopedAt(scope, MailFathomPermission.AdminRead);
        harness.Holding();

        // Act
        await harness.Roster.ReadRosterAsync(FirstPage, TestContext.Current.CancellationToken);

        // Assert
        await harness.Directory.Received(1).ReadUserPageAsync(
            FirstPage,
            Arg.Is<IReadOnlySet<AssignmentScope>>(scopes => scopes!.SetEquals(new[] { scope })),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [MemberData(nameof(ScopesCoveringTheOrganization))]
    public async Task ProvisionAsync_IntoAnOrganizationTheCallersScopeCovers_RecordsTheUserIntoIt(AssignmentScope scope)
    {
        // Arrange
        var harness = HarnessScopedAt(scope, MailFathomPermission.AdminConfigurationWrite);

        // Act
        var outcome = await harness.Roster.ProvisionAsync(
            "alex",
            AccessAuthorizations.ScopedOrganization,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.IsProvisioned);
        await harness.Provisioning.Received(1).ProvisionAsync(
            outcome.User,
            "alex",
            AccessAuthorizations.ScopedOrganization,
            Arg.Any<CancellationToken>());
    }

    /// <summary>An organization outside the caller's scope is refused exactly as one this deployment does not hold, so the refusal discloses nothing beyond the caller's own.</summary>
    [Theory]
    [MemberData(nameof(ScopesOutsideTheOrganization))]
    public async Task ProvisionAsync_IntoAnOrganizationOutsideTheCallersScope_IsRefusedAsNoSuchOrganizationWithoutRecordingAnybody(
        AssignmentScope scope)
    {
        // Arrange
        var harness = HarnessScopedAt(scope, MailFathomPermission.AdminConfigurationWrite);

        // Act
        var outcome = await harness.Roster.ProvisionAsync(
            "alex",
            AccessAuthorizations.ScopedOrganization,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.IsProvisioned);
        Assert.Contains(
            $"holds no organization '{AccessAuthorizations.ScopedOrganization}'",
            outcome.RefusalMessage!,
            StringComparison.Ordinal);
        Assert.Empty(harness.Provisioning.ReceivedCalls());
    }

    [Fact]
    public async Task ProvisionAsync_IntoAnOrganizationTheInsertFindsNoLonger_IsRefusedAsNoSuchOrganization()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.OrganizationGone();

        // Act
        var outcome = await harness.Roster.ProvisionAsync(
            "alex",
            AccessAuthorizations.ScopedOrganization,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.IsProvisioned);
        Assert.Contains(
            $"holds no organization '{AccessAuthorizations.ScopedOrganization}'",
            outcome.RefusalMessage!,
            StringComparison.Ordinal);
    }

    /// <summary>Only the deployment's scope covers a user in no organization, so only it records one there, and the refusal is the same whatever the request named.</summary>
    [Theory]
    [MemberData(nameof(ScopesBelowTheDeployment))]
    public async Task ProvisionAsync_IntoNoOrganizationByACallerScopedBelowTheDeployment_IsRefusedForTheDeploymentAloneWithoutRecordingAnybody(
        AssignmentScope scope)
    {
        // Arrange
        var harness = HarnessScopedAt(scope, MailFathomPermission.AdminConfigurationWrite);

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Roster.ProvisionAsync("alex", organizationId: null, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminConfigurationWrite, refusal.RequiredPermission);
        Assert.True(refusal.RefusedForTheDeploymentAlone);
        Assert.Empty(harness.Provisioning.ReceivedCalls());
    }

    [Theory]
    [MemberData(nameof(ScopesCoveringTheUser))]
    public async Task RelabelAsync_AUserTheCallersScopeCovers_PutsTheLabelOnTheirRow(AssignmentScope scope)
    {
        // Arrange
        var harness = HarnessScopedAt(scope, MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(new UserRecord(AccessAuthorizations.ScopedHolder, "alexandra"));

        // Act
        var outcome = await harness.Roster.RelabelAsync(
            AccessAuthorizations.ScopedHolder,
            "alex",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.IsWritten);
    }

    [Theory]
    [MemberData(nameof(ScopesOutsideTheUser))]
    public async Task RelabelAsync_AUserOutsideTheCallersScope_ReportsTheUserAsUnheldWithoutTouchingTheRow(AssignmentScope scope)
    {
        // Arrange
        var harness = HarnessScopedAt(scope, MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(new UserRecord(AccessAuthorizations.ScopedHolder, "alexandra"));

        // Act
        var outcome = await harness.Roster.RelabelAsync(
            AccessAuthorizations.ScopedHolder,
            "alex",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(UserRowWriteOutcome.NoSuchUser, outcome);
        Assert.Empty(harness.Provisioning.ReceivedCalls());
    }

    [Theory]
    [MemberData(nameof(ScopesCoveringTheUser))]
    public async Task EraseAsync_AUserTheCallersScopeCovers_ErasesThem(AssignmentScope scope)
    {
        // Arrange
        var harness = HarnessScopedAt(scope, MailFathomPermission.AdminErase);
        harness.Erasing(AccessAuthorizations.ScopedHolder);

        // Act
        var outcome = await harness.Roster.EraseAsync(AccessAuthorizations.ScopedHolder, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.UserErased);
    }

    /// <summary>A user outside the caller's scope is answered exactly as one this deployment does not hold, served or not.</summary>
    [Theory]
    [MemberData(nameof(ScopesOutsideTheUser))]
    public async Task EraseAsync_AUserOutsideTheCallersScope_ErasesNothingAndReportsNothingServed(AssignmentScope scope)
    {
        // Arrange
        var harness = HarnessScopedAt(scope, MailFathomPermission.AdminErase);
        harness.Serving(AccessAuthorizations.ScopedHolder);
        harness.Erasing(AccessAuthorizations.ScopedHolder);

        // Act
        var outcome = await harness.Roster.EraseAsync(AccessAuthorizations.ScopedHolder, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((false, false), (outcome.UserErased, outcome.WasServed));
        Assert.Empty(harness.Erasure.ReceivedCalls());
    }

    /// <summary>
    /// An erasure takes no mail account, so a mailbox assigned to the user alone that the caller's scope does not cover
    /// refuses nothing: the user goes, and the account stays behind for an administrator whose scope covers it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EraseAsync_AUserAloneAssignedAnAccountOutsideTheCallersScope_ErasesThemAnyway(bool scopedOverTheOrganization)
    {
        // Arrange
        var scope = scopedOverTheOrganization
            ? AssignmentScope.Organization(AccessAuthorizations.ScopedOrganization)
            : AssignmentScope.User(AccessAuthorizations.ScopedHolder);
        var harness = HarnessScopedAt(scope, MailFathomPermission.AdminErase);
        harness.Erasing(AccessAuthorizations.ScopedHolder);

        // Act
        var outcome = await harness.Roster.EraseAsync(AccessAuthorizations.ScopedHolder, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.UserErased);
        await harness.Erasure.Received(1).EraseAsync(AccessAuthorizations.ScopedHolder, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Whether the user was served is read before the erasure rather than after, because the answer must describe the
    /// deployment the caller asked about rather than the one the erasure left.
    /// </summary>
    [Fact]
    public async Task EraseAsync_AUserThisProcessIsServing_ReportsThatARestartIsOwed()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminErase);
        harness.Serving(SyntheticUser.Deployment);
        harness.Erasing(SyntheticUser.Deployment);

        // Act
        var outcome = await harness.Roster.EraseAsync(
            SyntheticUser.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.UserErased);
        Assert.True(outcome.WasServed);
    }

    /// <summary>A user whose record could not be read is reported as unserved rather than failing the erasure, which still runs.</summary>
    [Fact]
    public async Task EraseAsync_AUserWhoseRecordCannotBeRead_ErasesThemAndReportsThemUnserved()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminErase);
        harness.Erasing(SyntheticUser.Deployment);
        harness.UserRows.ReadAsync(SyntheticUser.Deployment, Arg.Any<CancellationToken>())
            .ThrowsAsync(new UserSettingsUnreadableException("The user records could not be read."));

        // Act
        var outcome = await harness.Roster.EraseAsync(
            SyntheticUser.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.UserErased);
        Assert.False(outcome.WasServed);
    }

    /// <summary>
    /// The user is off this replica's roster while the deletion runs, so a request served meanwhile no longer reaches
    /// them, and nothing about the mail accounts they were assigned is stopped or held for it.
    /// </summary>
    [Fact]
    public async Task EraseAsync_AUserThisProcessIsServing_IsUnservedWhileTheErasureRuns()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminErase);
        harness.Serving(SyntheticUser.Deployment);
        var servedWhileErasing = true;
        harness.Erasure
            .EraseAsync(SyntheticUser.Deployment, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                servedWhileErasing = harness.ServedUsers.Peek(SyntheticUser.Deployment) is not null;

                return true;
            });

        // Act
        var outcome = await harness.Roster.EraseAsync(
            SyntheticUser.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.UserErased);
        Assert.False(servedWhileErasing);
    }

    /// <summary>An erasure that fails erased nothing, so the person goes on being served and no replica is told otherwise.</summary>
    [Fact]
    public async Task EraseAsync_TheErasureFails_GoesOnServingThemAndAnnouncesNothing()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminErase);
        harness.Serving(SyntheticUser.Deployment);
        harness.Erasure
            .EraseAsync(SyntheticUser.Deployment, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("The database went away."));
        var heard = await RosterAnnouncementListener.ListenAsync(harness.Backplane, () => true);

        // Act
        var failure = await Record.ExceptionAsync(
            () => harness.Roster.EraseAsync(SyntheticUser.Deployment, TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<InvalidOperationException>(failure);
        Assert.NotNull(harness.ServedUsers.Peek(SyntheticUser.Deployment));
        Assert.Empty(heard);
    }

    [Fact]
    public async Task EraseAsync_AUserThisDeploymentDoesNotHold_ReportsThatNothingWasRemoved()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminErase);

        // Act
        var outcome = await harness.Roster.EraseAsync(
            SyntheticUser.Another,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.UserErased);
    }

    [Fact]
    public async Task EraseAsync_AUserNamingNobody_IsRefusedWithoutReachingTheErasure()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminErase);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => harness.Roster.EraseAsync(default, TestContext.Current.CancellationToken));

        await harness.Erasure.DidNotReceiveWithAnyArgs().EraseAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>Erasing somebody disposes of everything this deployment recorded about them, which is a grant of its own.</summary>
    [Fact]
    public async Task EraseAsync_ACallerHoldingOnlyTheAdministrativeConfigurationWrite_IsRefused()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Roster.EraseAsync(SyntheticUser.Deployment, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminErase, refusal.RequiredPermission);
    }

    /// <summary>A label is what an administrator selects a user by, and nothing is keyed by it, so replacing one is an ordinary write.</summary>
    [Fact]
    public async Task RelabelAsync_AUserThisDeploymentHolds_PutsTheLabelOnTheirRow()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(new UserRecord(SyntheticUser.Deployment, "alexandra"));

        // Act
        var outcome = await harness.Roster.RelabelAsync(
            SyntheticUser.Deployment,
            "alex",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.IsWritten);
        await harness.Provisioning.Received(1)
            .RelabelAsync(SyntheticUser.Deployment, "alex", Arg.Any<CancellationToken>());
    }

    /// <summary>The label is trimmed the way a declared one is, so a roster is never told apart by trailing space.</summary>
    [Fact]
    public async Task RelabelAsync_ALabelWrittenWithSurroundingSpace_WritesItTrimmed()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(new UserRecord(SyntheticUser.Deployment, "alexandra"));

        // Act
        await harness.Roster.RelabelAsync(
            SyntheticUser.Deployment,
            "  alex  ",
            TestContext.Current.CancellationToken);

        // Assert
        await harness.Provisioning.Received(1)
            .RelabelAsync(SyntheticUser.Deployment, "alex", Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A user erased between being read and the relabel reaching their row is reported as one this deployment does not
    /// hold, which is what the statement writing no row means now that no label is ever refused.
    /// </summary>
    [Fact]
    public async Task RelabelAsync_AUserErasedBeforeTheWrite_ReportsTheUserAsUnheld()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(new UserRecord(SyntheticUser.Deployment, "alexandra"));
        harness.Provisioning
            .RelabelAsync(Arg.Any<UserId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var outcome = await harness.Roster.RelabelAsync(
            SyntheticUser.Deployment,
            "alex",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(UserRowWriteOutcome.NoSuchUser, outcome);
    }

    /// <summary>The roster reads a user's type and title back, so an administrator sees what the member directory shows.</summary>
    [Fact]
    public async Task ReadRosterAsync_AServiceAccountWithATitle_ReportsBoth()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminRead);
        harness.Holding(new UserRecord(SyntheticUser.Deployment, "backup-robot")
        {
            Type = UserType.ServiceAccount,
            JobTitle = "Nightly backup",
        });

        // Act
        var roster = await harness.Roster.ReadRosterAsync(FirstPage, TestContext.Current.CancellationToken);

        // Assert
        var entry = Assert.Single(roster.Entries);
        Assert.Equal(UserType.ServiceAccount, entry.Type);
        Assert.Equal("Nightly backup", entry.JobTitle);
    }

    /// <summary>A title is written trimmed, and a blank one takes the title off rather than storing nothing visible.</summary>
    [Theory]
    [InlineData("  Controller  ", "Controller")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public async Task SetJobTitleAsync_AUserThisDeploymentHolds_WritesTheTitleTrimmedOrNone(string? written, string? stored)
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(new UserRecord(SyntheticUser.Deployment, "alex"));

        // Act
        var outcome = await harness.Roster.SetJobTitleAsync(SyntheticUser.Deployment, written, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.IsWritten);
        await harness.Provisioning.Received(1).SetJobTitleAsync(SyntheticUser.Deployment, stored, Arg.Any<CancellationToken>());
    }

    /// <summary>A title past the column is refused with the bound rather than cut short where nobody sees it.</summary>
    [Fact]
    public async Task SetJobTitleAsync_ATitlePastTheBound_IsRefusedWithoutWriting()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(new UserRecord(SyntheticUser.Deployment, "alex"));

        // Act
        var outcome = await harness.Roster.SetJobTitleAsync(
            SyntheticUser.Deployment,
            new string('a', UserRecord.MaximumJobTitleLength + 1),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.IsWritten);
        Assert.Contains($"{UserRecord.MaximumJobTitleLength}", outcome.RefusalMessage, StringComparison.Ordinal);
        await harness.Provisioning.DidNotReceiveWithAnyArgs().SetJobTitleAsync(default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>A user outside the administrator's scope is answered as one the deployment does not hold, as a relabel is.</summary>
    [Theory]
    [MemberData(nameof(ScopesOutsideTheUser))]
    public async Task SetJobTitleAsync_AUserOutsideTheCallersScope_ReportsTheUserAsUnheldWithoutTouchingTheRow(AssignmentScope scope)
    {
        // Arrange
        var harness = HarnessScopedAt(scope, MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(new UserRecord(AccessAuthorizations.ScopedHolder, "alexandra"));

        // Act
        var outcome = await harness.Roster.SetJobTitleAsync(AccessAuthorizations.ScopedHolder, "Controller", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(UserRowWriteOutcome.NoSuchUser, outcome);
        Assert.Empty(harness.Provisioning.ReceivedCalls());
    }

    /// <summary>Writing a title or a type is configuration, so the reading grant reaches neither.</summary>
    [Fact]
    public async Task SetTypeAsync_ACallerWithoutTheConfigurationGrant_IsRefused()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminRead);
        harness.Holding(new UserRecord(SyntheticUser.Deployment, "alex"));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Roster.SetTypeAsync(SyntheticUser.Deployment, UserType.ServiceAccount, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminConfigurationWrite, refusal.RequiredPermission);
        Assert.Empty(harness.Provisioning.ReceivedCalls());
    }

    /// <summary>Marking a user a technical account is one write on their row, which is what takes them out of the member directory.</summary>
    [Fact]
    public async Task SetTypeAsync_AUserThisDeploymentHolds_WritesTheType()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(new UserRecord(SyntheticUser.Deployment, "backup-robot"));

        // Act
        var outcome = await harness.Roster.SetTypeAsync(SyntheticUser.Deployment, UserType.ServiceAccount, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.IsWritten);
        await harness.Provisioning.Received(1).SetTypeAsync(SyntheticUser.Deployment, UserType.ServiceAccount, Arg.Any<CancellationToken>());
    }

    /// <summary>A user outside the administrator's scope is answered as one the deployment does not hold, so nobody's directory is changed from outside it.</summary>
    [Theory]
    [MemberData(nameof(ScopesOutsideTheUser))]
    public async Task SetTypeAsync_AUserOutsideTheCallersScope_ReportsTheUserAsUnheldWithoutTouchingTheRow(AssignmentScope scope)
    {
        // Arrange
        var harness = HarnessScopedAt(scope, MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(new UserRecord(AccessAuthorizations.ScopedHolder, "alexandra"));

        // Act
        var outcome = await harness.Roster.SetTypeAsync(AccessAuthorizations.ScopedHolder, UserType.ServiceAccount, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(UserRowWriteOutcome.NoSuchUser, outcome);
        Assert.Empty(harness.Provisioning.ReceivedCalls());
    }

    [Fact]
    public async Task SetManagerAsync_AManagerTheStoreAccepts_IsWritten()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(new UserRecord(SyntheticUser.Deployment, "alex"));

        // Act
        var outcome = await harness.Roster.SetManagerAsync(SyntheticUser.Deployment, SyntheticUser.Another, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.IsWritten);
        await harness.Provisioning.Received(1).SetManagerAsync(SyntheticUser.Deployment, SyntheticUser.Another, Arg.Any<CancellationToken>());
    }

    /// <summary>Each rule the store refused on is named to the administrator, so they know what to change rather than only that something was wrong.</summary>
    [Theory]
    [InlineData(ManagerAssignmentResult.OwnManager, "never their own manager")]
    [InlineData(ManagerAssignmentResult.UserInNoOrganization, "belongs to no organization")]
    [InlineData(ManagerAssignmentResult.UnknownManager, "not a member of this user's organization")]
    [InlineData(ManagerAssignmentResult.ManagerOutsideOrganization, "not a member of this user's organization")]
    [InlineData(ManagerAssignmentResult.ManagerNotAPerson, "service account")]
    [InlineData(ManagerAssignmentResult.ReportingCycle, "already reports to this user")]
    public async Task SetManagerAsync_AManagerTheStoreRefuses_IsRefusedNamingTheRule(ManagerAssignmentResult refused, string rule)
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(new UserRecord(SyntheticUser.Deployment, "alex"));
        harness.Provisioning
            .SetManagerAsync(Arg.Any<UserId>(), Arg.Any<UserId?>(), Arg.Any<CancellationToken>())
            .Returns(refused);

        // Act
        var outcome = await harness.Roster.SetManagerAsync(SyntheticUser.Deployment, SyntheticUser.Another, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.UserHeld);
        Assert.Contains(rule, outcome.RefusalMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetManagerAsync_AUserTheStoreNoLongerHolds_ReportsTheUserAsUnheld()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(new UserRecord(SyntheticUser.Deployment, "alex"));
        harness.Provisioning
            .SetManagerAsync(Arg.Any<UserId>(), Arg.Any<UserId?>(), Arg.Any<CancellationToken>())
            .Returns(ManagerAssignmentResult.UnknownUser);

        // Act
        var outcome = await harness.Roster.SetManagerAsync(SyntheticUser.Deployment, manager: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(UserRowWriteOutcome.NoSuchUser, outcome);
    }

    /// <summary>A user outside the administrator's scope is answered as one the deployment does not hold, as a title is.</summary>
    [Theory]
    [MemberData(nameof(ScopesOutsideTheUser))]
    public async Task SetManagerAsync_AUserOutsideTheCallersScope_ReportsTheUserAsUnheldWithoutTouchingTheRow(AssignmentScope scope)
    {
        // Arrange
        var harness = HarnessScopedAt(scope, MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(new UserRecord(AccessAuthorizations.ScopedHolder, "alexandra"));

        // Act
        var outcome = await harness.Roster.SetManagerAsync(AccessAuthorizations.ScopedHolder, manager: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(UserRowWriteOutcome.NoSuchUser, outcome);
        Assert.Empty(harness.Provisioning.ReceivedCalls());
    }

    /// <summary>Who reports to whom is configuration an administrator states, so the reading grant does not reach it.</summary>
    [Fact]
    public async Task SetManagerAsync_ACallerWithoutTheConfigurationGrant_IsRefused()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminRead);
        harness.Holding(new UserRecord(SyntheticUser.Deployment, "alex"));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Roster.SetManagerAsync(SyntheticUser.Deployment, SyntheticUser.Another, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminConfigurationWrite, refusal.RequiredPermission);
        Assert.Empty(harness.Provisioning.ReceivedCalls());
    }

    /// <summary>The roster lists the manager each user names, so an administrator reads the structure where they write it.</summary>
    [Fact]
    public async Task ReadRosterAsync_AUserNamingAManager_ReportsTheManager()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminRead);
        harness.Holding(new UserRecord(SyntheticUser.Deployment, "alex") { Manager = SyntheticUser.Another });

        // Act
        var roster = await harness.Roster.ReadRosterAsync(FirstPage, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SyntheticUser.Another, Assert.Single(roster.Entries).Manager);
    }

    /// <summary>Writing a title is configuration, so the reading grant does not reach it.</summary>
    [Fact]
    public async Task SetJobTitleAsync_ACallerWithoutTheConfigurationGrant_IsRefused()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminRead);
        harness.Holding(new UserRecord(SyntheticUser.Deployment, "alex"));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Roster.SetJobTitleAsync(SyntheticUser.Deployment, "Controller", TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminConfigurationWrite, refusal.RequiredPermission);
        Assert.Empty(harness.Provisioning.ReceivedCalls());
    }

    /// <summary>A user this deployment does not hold is an absence to report rather than a label to refuse.</summary>
    /// <remarks>
    /// The two are one sentence to an administrator and two answers to a caller: the route publishes this one as the
    /// same absence every other user-scoped route answers with, so the outcome carries which of them it is.
    /// </remarks>
    [Fact]
    public async Task RelabelAsync_AUserThisDeploymentDoesNotHold_ReportsTheUserAsUnheld()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(new UserRecord(SyntheticUser.Deployment, "alex"));

        // Act
        var outcome = await harness.Roster.RelabelAsync(
            SyntheticUser.Another,
            "sam",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.UserHeld);
        Assert.False(outcome.IsWritten);
        await harness.Provisioning.DidNotReceiveWithAnyArgs()
            .RelabelAsync(default, default!, CancellationToken.None);
    }

    /// <summary>A label is the one thing an administrator reads a roster by, so an empty one leaves nothing to read.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RelabelAsync_ALabelNamingNothing_IsRefusedWithoutReadingTheRoster(string? label)
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);

        // Act
        var outcome = await harness.Roster.RelabelAsync(
            SyntheticUser.Deployment,
            label,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.UserHeld);
        Assert.NotNull(outcome.RefusalMessage);
        await harness.Directory.DidNotReceiveWithAnyArgs()
            .ReadUserAsync(default, CancellationToken.None);
    }

    [Fact]
    public async Task RelabelAsync_AUserNamingNobody_IsRefusedWithoutReachingTheRow()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => harness.Roster.RelabelAsync(default, "alex", TestContext.Current.CancellationToken));

        await harness.Provisioning.DidNotReceiveWithAnyArgs()
            .RelabelAsync(default, default!, CancellationToken.None);
    }

    /// <summary>Changing what a roster reads like is what this deployment is rather than what it does next, so it takes the configuration grant.</summary>
    [Fact]
    public async Task RelabelAsync_ACallerHoldingOnlyTheAdministrativeRead_IsRefused()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminRead);

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Roster.RelabelAsync(
                SyntheticUser.Deployment,
                "alex",
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminConfigurationWrite, refusal.RequiredPermission);
    }

    /// <summary>An administrator reads which endpoints each user is served on where they select the user, so the roster carries both switches.</summary>
    [Fact]
    public async Task ReadRosterAsync_AUserKeptOffAnEndpoint_ReportsTheirSwitches()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminRead);
        harness.Holding(new UserRecord(SyntheticUser.Deployment, "alex")
        {
            EndpointAccess = new UserEndpointAccess(McpEndpoint: false, ClientEndpoint: true),
        });

        // Act
        var roster = await harness.Roster.ReadRosterAsync(FirstPage, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            new UserEndpointAccess(McpEndpoint: false, ClientEndpoint: true),
            Assert.Single(roster.Entries).EndpointAccess);
    }

    /// <summary>The roster for an administrator holding one permission at one scope, over <see cref="AccessAuthorizations.ScopedHolder" /> placed in the scoped organization.</summary>
    private static RosterHarness HarnessScopedAt(AssignmentScope scope, MailFathomPermission granted) =>
        new(AccessAuthorizations.ForAdministratorScopedAt(scope, granted));

    /// <summary>The roster over substituted rows, with the endpoint posture a deployment's several-user refusal is read from.</summary>
    private sealed class RosterHarness
    {
        internal RosterHarness(
            MailFathomPermission granted,
            ClientEndpointOptions? clientEndpoint = null)
            : this(
                AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Caller(AdministratorIdentity, [granted])),
                clientEndpoint)
        {
        }

        internal RosterHarness(
            AccessAuthorization authorization,
            ClientEndpointOptions? clientEndpoint = null)
        {
            this.Directory = Substitute.For<IUserDirectory>();
            this.Directory.ReadUsersAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

            this.Provisioning = Substitute.For<IUserProvisioning>();
            this.Provisioning
                .ProvisionAsync(Arg.Any<UserId>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
                .Returns(UserProvisioningResult.Provisioned);
            this.Provisioning
                .RelabelAsync(Arg.Any<UserId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(true);
            this.Provisioning
                .SetJobTitleAsync(Arg.Any<UserId>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(true);
            this.Provisioning
                .SetTypeAsync(Arg.Any<UserId>(), Arg.Any<UserType>(), Arg.Any<CancellationToken>())
                .Returns(true);

            this.Erasure = Substitute.For<IUserErasure>();
            this.Erasure.EraseAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>()).Returns(false);

            this.Documents = Substitute.For<IRecordedUserRecordCommits>();
            this.Writer = new ComposingUserRecordWriter(this.Documents);
            this.Documents
                .CommitAsync(
                    Arg.Any<UserId>(),
                    Arg.Any<string>(),
                    Arg.Any<UserEndpointAccess>(),
                    Arg.Any<long>(),
                    Arg.Any<CancellationToken>())
                .Returns((long?)2);

            // No user's record is held unless a test states it, so "served" is a fact a test states rather than a default.
            this.UserRows = Substitute.For<IUserSettingsDocumentReader>();
            this.UserRows.ReadVersionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
            this.UserRows.ReadVersionsAsync(Arg.Any<IReadOnlyCollection<UserId>>(), Arg.Any<CancellationToken>()).Returns([]);
            this.ServedUsers = ResolvedServedUsers.Over(this.UserRows);

            this.Roster = new UserRosterAdministration(
                authorization,
                this.Directory,
                this.Provisioning,
                this.Erasure,
                this.Writer,
                new UserAccountDocumentBinder(
                    new PersistedSecretMaterial(DeclaredSecretScheme.Registered),
                    new FakeTimeProvider(),
                    Options.Create(new SensitiveContentOptions()),
                    Options.Create(new DecisionModelOptions())),
                this.ServedUsers,
                new SeveralUserAdmission(
                    Options.Create(new McpEndpointOptions()),
                    Options.Create(clientEndpoint ?? new ClientEndpointOptions())),
                new ConfigurationChangeAnnouncements(
                    () => Task.FromResult(this.Backplane.Connect()),
                    NullLogger<ConfigurationChangeAnnouncements>.Instance),
                NullLogger<UserRosterAdministration>.Instance);
        }

        internal UserRosterAdministration Roster { get; }

        /// <summary>Gets the backplane a roster change is announced over, which nobody hears until a test listens.</summary>
        internal InMemoryBackplane Backplane { get; } = new();

        internal IUserDirectory Directory { get; }

        internal IUserProvisioning Provisioning { get; }

        internal IUserErasure Erasure { get; }

        /// <summary>Gets what each commit was composed to, which is what a test arranges an answer on and asserts against.</summary>
        internal IRecordedUserRecordCommits Documents { get; }

        /// <summary>Gets the writer the roster commits through, which composes each commit under the policies a test states.</summary>
        internal ComposingUserRecordWriter Writer { get; }

        /// <summary>Gets the user rows the served users are read from, which hold nobody's record until a test says so.</summary>
        internal IUserSettingsDocumentReader UserRows { get; }

        internal ServedUsers ServedUsers { get; }

        internal void Holding(params UserRecord[] held)
        {
            this.Directory.ReadUsersAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(held);
            this.Directory.ReadUserPageAsync(
                    Arg.Any<AdministrativeListingQuery>(),
                    Arg.Any<IReadOnlySet<AssignmentScope>>(),
                    Arg.Any<CancellationToken>())
                .Returns(new AdministrativeListingPage<UserRecord>(held, ContinuesAfter: null));

            foreach (var record in held)
            {
                this.Directory.ReadUserAsync(record.User, Arg.Any<CancellationToken>()).Returns(record);
            }
        }

        internal void Serving(UserId user) => this.ServedUsers.Published(new ServedUser(user, "served", []), 1);

        /// <summary>Has the insert find the organization it was to record the user into gone, as it does one deleted while the write was in flight.</summary>
        internal void OrganizationGone() =>
            this.Provisioning
                .ProvisionAsync(Arg.Any<UserId>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
                .Returns(UserProvisioningResult.UnknownOrganization);

        /// <summary>Counts how often the users this deployment holds were counted, which a provisioning or an erasure does once it commits.</summary>
        internal int CountsRead() =>
            this.UserRows.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IUserSettingsDocumentReader.ReadVersionsAsync));

        internal void Erasing(UserId user) =>
            this.Erasure.EraseAsync(user, Arg.Any<CancellationToken>()).Returns(true);
    }
}
