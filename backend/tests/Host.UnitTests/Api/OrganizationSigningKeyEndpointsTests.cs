// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access.Organizations.SigningKeys;
using MailFathom.Application.Paging;
using MailFathom.Domain.Access;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the signing key routes answer, and that a listing names each key's state and never its material.</summary>
public sealed class OrganizationSigningKeyEndpointsTests
{
    private static readonly Guid OrganizationId = new("44444444-4444-4444-8444-444444444444");

    private static readonly Guid ActiveKeyId = new("0199a0c0-0000-7000-8000-0000000000a1");

    private static readonly Guid RetiredKeyId = new("0199a0c0-0000-7000-8000-0000000000a0");

    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ListAsync_AnOrganizationsKeys_NamesEachKeysStateInTheOrderTheyWereMinted()
    {
        // Arrange
        var keys = Substitute.For<IOrganizationSigningKeyStore>();
        keys.ReadAsync(OrganizationId, Arg.Any<AdministrativeListingQuery>(), Arg.Any<CancellationToken>()).Returns(new OrganizationSigningKeyListing(
            OrganizationId,
            "Acme Corporation",
            [
                new OrganizationSigningKey(RetiredKeyId, Now.AddDays(-365), RetiredAt: Now, RevokedAt: Now),
                new OrganizationSigningKey(ActiveKeyId, Now, RetiredAt: null, RevokedAt: null),
            ],
            ContinuesAfter: null));

        // Act
        var result = await OrganizationSigningKeyEndpoints.ListAsync(
            OrganizationId,
            pageSize: null,
            cursor: null,
            Administration(keys, MailFathomPermission.AdminRead),
            TestContext.Current.CancellationToken);

        // Assert
        var listing = Assert.IsType<Ok<OrganizationSigningKeyListResponse>>(result.Result).Value!;
        Assert.Equal("Acme Corporation", listing.DisplayName);
        Assert.Equal(
            [(RetiredKeyId, "revoked"), (ActiveKeyId, "active")],
            listing.Keys.Select(key => (key.KeyId, key.State)));
        Assert.Null(listing.NextCursor);
    }

    /// <summary>A page that is not the last names the cursor the next one is asked with, and only this organization's listing accepts it.</summary>
    [Fact]
    public async Task ListAsync_APageThatIsNotTheLast_HandsBackACursorTheNextRequestIsReadFrom()
    {
        // Arrange
        var keys = Substitute.For<IOrganizationSigningKeyStore>();
        keys.ReadAsync(OrganizationId, Arg.Any<AdministrativeListingQuery>(), Arg.Any<CancellationToken>()).Returns(new OrganizationSigningKeyListing(
            OrganizationId,
            "Acme Corporation",
            [new OrganizationSigningKey(RetiredKeyId, Now.AddDays(-365), RetiredAt: Now, RevokedAt: null)],
            ContinuesAfter: RetiredKeyId));
        var administration = Administration(keys, MailFathomPermission.AdminRead);

        var first = await OrganizationSigningKeyEndpoints.ListAsync(
            OrganizationId,
            pageSize: 1,
            cursor: null,
            administration,
            TestContext.Current.CancellationToken);
        var cursor = Assert.IsType<Ok<OrganizationSigningKeyListResponse>>(first.Result).Value!.NextCursor;

        // Act
        await OrganizationSigningKeyEndpoints.ListAsync(
            OrganizationId,
            pageSize: 1,
            cursor,
            administration,
            TestContext.Current.CancellationToken);
        var elsewhere = await OrganizationSigningKeyEndpoints.ListAsync(
            new Guid("55555555-5555-4555-8555-555555555555"),
            pageSize: 1,
            cursor,
            administration,
            TestContext.Current.CancellationToken);

        // Assert
        await keys.Received(1).ReadAsync(
            OrganizationId,
            Arg.Is<AdministrativeListingQuery>(query => query.After == RetiredKeyId),
            Arg.Any<CancellationToken>());
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(elsewhere.Result).StatusCode);
    }

    [Fact]
    public async Task ListAsync_AnOrganizationTheDeploymentDoesNotHold_IsNotFound()
    {
        // Arrange
        var keys = Substitute.For<IOrganizationSigningKeyStore>();

        // Act
        var result = await OrganizationSigningKeyEndpoints.ListAsync(
            OrganizationId,
            pageSize: null,
            cursor: null,
            Administration(keys, MailFathomPermission.AdminRead),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound<ProblemDetails>>(result.Result);
    }

    [Fact]
    public async Task RotateAsync_ADeploymentWithNoKeyRing_IsAConflictNamingTheSectionToConfigure()
    {
        // Arrange
        var keys = Substitute.For<IOrganizationSigningKeyStore>();
        keys.ReplaceActiveKeyAsync(OrganizationId, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(OrganizationSigningKeyReplacement.Of(OrganizationSigningKeyReplacementOutcome.NoKeyRing));

        // Act
        var result = await OrganizationSigningKeyEndpoints.RotateAsync(
            OrganizationId,
            Administration(keys, MailFathomPermission.AdminCredentialsWrite),
            TestContext.Current.CancellationToken);

        // Assert
        var problem = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Contains("'DataEncryption'", problem.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RotateAsync_AnActiveKeyReplaced_NamesTheNewKeyAndTheRetiredOne()
    {
        // Arrange
        var keys = Substitute.For<IOrganizationSigningKeyStore>();
        keys.ReplaceActiveKeyAsync(OrganizationId, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new OrganizationSigningKeyReplacement(OrganizationSigningKeyReplacementOutcome.Replaced, ActiveKeyId, RetiredKeyId));

        // Act
        var result = await OrganizationSigningKeyEndpoints.RotateAsync(
            OrganizationId,
            Administration(keys, MailFathomPermission.AdminCredentialsWrite),
            TestContext.Current.CancellationToken);

        // Assert
        var rotated = Assert.IsType<Ok<OrganizationSigningKeyRotatedResponse>>(result.Result).Value;
        Assert.Equal(new OrganizationSigningKeyRotatedResponse(ActiveKeyId, RetiredKeyId), rotated);
    }

    [Theory]
    [InlineData(OrganizationSigningKeyRevocationOutcome.AlreadyRevoked, StatusCodes.Status409Conflict)]
    [InlineData(OrganizationSigningKeyRevocationOutcome.UnknownKey, StatusCodes.Status404NotFound)]
    public async Task RevokeAsync_ARevocationThatChangedNothing_SaysWhy(
        OrganizationSigningKeyRevocationOutcome outcome,
        int expectedStatus)
    {
        // Arrange
        var keys = Substitute.For<IOrganizationSigningKeyStore>();
        keys.RevokeAsync(OrganizationId, ActiveKeyId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(OrganizationSigningKeyRevocation.Of(outcome));

        // Act
        var result = await OrganizationSigningKeyEndpoints.RevokeAsync(
            OrganizationId,
            ActiveKeyId,
            Administration(keys, MailFathomPermission.AdminCredentialsWrite),
            TestContext.Current.CancellationToken);

        // Assert
        var status = result.Result switch
        {
            ProblemHttpResult problem => problem.StatusCode,
            NotFound<ProblemDetails> notFound => notFound.StatusCode,
            _ => 0,
        };
        Assert.Equal(expectedStatus, status);
    }

    [Fact]
    public async Task RevokeAsync_TheActiveKey_NamesTheReplacementMintedWithIt()
    {
        // Arrange
        var keys = Substitute.For<IOrganizationSigningKeyStore>();
        keys.RevokeAsync(OrganizationId, RetiredKeyId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new OrganizationSigningKeyRevocation(OrganizationSigningKeyRevocationOutcome.Revoked, WasActive: true, ActiveKeyId));

        // Act
        var result = await OrganizationSigningKeyEndpoints.RevokeAsync(
            OrganizationId,
            RetiredKeyId,
            Administration(keys, MailFathomPermission.AdminCredentialsWrite),
            TestContext.Current.CancellationToken);

        // Assert
        var revoked = Assert.IsType<Ok<OrganizationSigningKeyRevokedResponse>>(result.Result).Value;
        Assert.Equal(new OrganizationSigningKeyRevokedResponse(RetiredKeyId, WasActive: true, ActiveKeyId), revoked);
    }

    private static OrganizationSigningKeyAdministration Administration(
        IOrganizationSigningKeyStore keys,
        MailFathomPermission granted) =>
        new(
            AccessAuthorizations.ForAdministratorGranted(granted),
            keys,
            Substitute.For<IOrganizationSigningKeyAuditor>(),
            new FakeTimeProvider(Now));
}
