// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access.Organizations.SigningKeys;
using MailFathom.Application.Paging;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Lists an organization's signing keys, rotates its active key ahead of schedule, and revokes one.</summary>
/// <remarks>
/// <para>
/// Listing is <see cref="MailFathomPermission.AdminRead" /> at a scope covering the organization; rotating and revoking
/// are <see cref="MailFathomPermission.AdminCredentialsWrite" /> over the deployment, for the reasons
/// <see cref="OrganizationSigningKeyAdministration" /> gives. No route takes or returns key material: a key is named by
/// its identifier, which is also the selector its signatures carry and is not a secret.
/// </para>
/// </remarks>
internal static class OrganizationSigningKeyEndpoints
{
    /// <summary>The route one organization's signing keys are listed at.</summary>
    internal const string SigningKeysRoute = $"{OrganizationEndpoints.OrganizationRoute}/signing-keys";

    /// <summary>The route an organization's active signing key is rotated at.</summary>
    internal const string RotationRoute = $"{SigningKeysRoute}/rotation";

    /// <summary>The route one of an organization's signing keys is revoked at.</summary>
    internal const string RevocationRoute = $"{SigningKeysRoute}/{{keyId:guid}}/revocation";

    /// <summary>Maps the signing key routes into the administrative group, so they inherit its authorization.</summary>
    /// <param name="api">The administrative route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapOrganizationSigningKeys(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(SigningKeysRoute, ListAsync)
            .RequirePermissionOverTarget(MailFathomPermission.AdminRead);

        api.MapPost(RotationRoute, RotateAsync)
            .RequirePermission(MailFathomPermission.AdminCredentialsWrite);

        api.MapPost(RevocationRoute, RevokeAsync)
            .RequirePermission(MailFathomPermission.AdminCredentialsWrite);
    }

    /// <summary>Lists one page of an organization's signing keys, in the order they were minted.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="pageSize">How many keys the page may hold, or <see langword="null" /> for the default.</param>
    /// <param name="cursor">The cursor the previous page returned, or <see langword="null" /> for the first page.</param>
    /// <param name="keys">The signing key administration.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the organization's display name, the page's keys, and the cursor the following page is asked with; <c>404</c> when no such organization exists within the caller's scope; or <c>400</c> naming what was wrong with the request.</returns>
    internal static async Task<Results<Ok<OrganizationSigningKeyListResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> ListAsync(
        Guid organizationId,
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromServices] OrganizationSigningKeyAdministration keys,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keys);

        if (!AdminListingRequest.TryResolve(
                AdministrativeListing.OrganizationSigningKeys,
                pageSize,
                cursor,
                out var query,
                out var refusal,
                within: organizationId))
        {
            return refusal;
        }

        if (await keys.ReadAsync(organizationId, query, cancellationToken) is not { } listing)
        {
            return NoSuchOrganization();
        }

        return TypedResults.Ok(OrganizationSigningKeyListResponse.For(
            listing,
            AdminListingRequest.NextCursor(AdministrativeListing.OrganizationSigningKeys, listing.ContinuesAfter, within: organizationId)));
    }

    /// <summary>Mints a fresh active key for an organization now, retiring the one it signs with.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="keys">The signing key administration.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> naming the active key and the one it retired, <c>404</c> when no such organization exists, or <c>409</c> when the deployment configures no key ring to seal a key under.</returns>
    internal static async Task<Results<Ok<OrganizationSigningKeyRotatedResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> RotateAsync(
        Guid organizationId,
        [FromServices] OrganizationSigningKeyAdministration keys,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var replacement = await keys.RotateAsync(organizationId, cancellationToken);

        return replacement.Outcome switch
        {
            OrganizationSigningKeyReplacementOutcome.Replaced or OrganizationSigningKeyReplacementOutcome.AlreadyCurrent
                when replacement.ActiveKeyId is { } activeKeyId =>
                TypedResults.Ok(new OrganizationSigningKeyRotatedResponse(activeKeyId, replacement.RetiredKeyId)),
            OrganizationSigningKeyReplacementOutcome.NoKeyRing => NoKeyRing(),
            _ => NoSuchOrganization(),
        };
    }

    /// <summary>Revokes one of an organization's signing keys; revoking the active one mints its replacement.</summary>
    /// <param name="organizationId">The organization the key belongs to.</param>
    /// <param name="keyId">The key.</param>
    /// <param name="keys">The signing key administration.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> naming the revoked key and the organization's active key afterwards, <c>404</c> when the organization holds no such key, or <c>409</c> when the key was already revoked.</returns>
    internal static async Task<Results<Ok<OrganizationSigningKeyRevokedResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> RevokeAsync(
        Guid organizationId,
        Guid keyId,
        [FromServices] OrganizationSigningKeyAdministration keys,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var revocation = await keys.RevokeAsync(organizationId, keyId, cancellationToken);

        return revocation.Outcome switch
        {
            OrganizationSigningKeyRevocationOutcome.Revoked => TypedResults.Ok(
                new OrganizationSigningKeyRevokedResponse(keyId, revocation.WasActive, revocation.ReplacementKeyId)),
            OrganizationSigningKeyRevocationOutcome.AlreadyRevoked => TypedResults.Problem(
                $"Signing key '{keyId}' is already revoked, so nothing changed.",
                statusCode: StatusCodes.Status409Conflict),
            _ => TypedResults.NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Detail = "This organization holds no such signing key. List its signing keys to read the identifiers it does hold.",
            }),
        };
    }

    private static ProblemHttpResult NoKeyRing() => TypedResults.Problem(
        "This deployment configures no data-encryption key ring, so a signing key's private half cannot be sealed and "
        + "none was minted. Configure 'DataEncryption' and rotate again.",
        statusCode: StatusCodes.Status409Conflict);

    private static NotFound<ProblemDetails> NoSuchOrganization() => TypedResults.NotFound(new ProblemDetails
    {
        Status = StatusCodes.Status404NotFound,
        Detail = "This deployment holds no such organization.",
    });
}
