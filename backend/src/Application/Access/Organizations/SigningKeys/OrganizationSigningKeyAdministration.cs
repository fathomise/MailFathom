// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Paging;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Organizations.SigningKeys;

/// <summary>What a deployment administrator does to an organization's signing keys: list them, rotate ahead of schedule, and revoke one.</summary>
/// <remarks>
/// <para>
/// Listing is <see cref="MailFathomPermission.AdminRead" /> at a scope covering the organization, because it carries no
/// key material and an organization's own administrator may see whether its signing is healthy. Rotating and revoking
/// are <see cref="MailFathomPermission.AdminCredentialsWrite" /> over the whole deployment: a signing key is a credential
/// the organization's members are recognized by, and revoking one changes what every receiving account trusts.
/// </para>
/// <para>
/// Both writes are audited through <see cref="IOrganizationSigningKeyAuditor" /> once they take effect. An act that
/// changed nothing — a rotation that found no key ring, a revocation of a key already revoked — is not, because nothing
/// happened that an auditor would look for.
/// </para>
/// </remarks>
public sealed class OrganizationSigningKeyAdministration
{
    private readonly AccessAuthorization authorization;
    private readonly IOrganizationSigningKeyStore keys;
    private readonly IOrganizationSigningKeyAuditor auditor;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the administration over one deployment's organization signing keys.</summary>
    /// <param name="authorization">What each act is admitted by.</param>
    /// <param name="keys">Where the keys are kept.</param>
    /// <param name="auditor">Where a rotation or revocation is written down.</param>
    /// <param name="timeProvider">The clock an act is stamped with.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public OrganizationSigningKeyAdministration(
        AccessAuthorization authorization,
        IOrganizationSigningKeyStore keys,
        IOrganizationSigningKeyAuditor auditor,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(auditor);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.authorization = authorization;
        this.keys = keys;
        this.auditor = auditor;
        this.timeProvider = timeProvider;
    }

    /// <summary>Reads one page of an organization's signing keys, in identifier order.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="query">The page to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The keys beside the organization's display name, or <see langword="null" /> when the deployment holds no such organization or the caller's scope does not cover it.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller holds <see cref="MailFathomPermission.AdminRead" /> at no scope.</exception>
    public async Task<OrganizationSigningKeyListing?> ReadAsync(
        Guid organizationId,
        AdministrativeListingQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        this.authorization.RequirePermissionAtAnyScope(MailFathomPermission.AdminRead);

        var covered = await this.authorization.PermitsOverScopeAsync(
            MailFathomPermission.AdminRead,
            AssignmentScope.Organization(organizationId),
            cancellationToken);

        return covered ? await this.keys.ReadAsync(organizationId, query, cancellationToken) : null;
    }

    /// <summary>Mints a fresh active key for an organization now, retiring the one it signs with.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the rotation did.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.AdminCredentialsWrite" /> over the deployment.</exception>
    /// <remarks>An organization with no active key — one created while the deployment had no key ring — is given one, which is the same act.</remarks>
    public async Task<OrganizationSigningKeyReplacement> RotateAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminCredentialsWrite);

        var at = this.timeProvider.GetUtcNow();
        var replacement = await this.keys.ReplaceActiveKeyAsync(organizationId, at, at, cancellationToken);

        if (replacement.Outcome == OrganizationSigningKeyReplacementOutcome.Replaced)
        {
            await this.RecordAsync(
                OrganizationSigningKeyAct.Rotated,
                organizationId,
                replacement.RetiredKeyId,
                replacement.ActiveKeyId,
                at,
                cancellationToken);
        }

        return replacement;
    }

    /// <summary>Revokes one of an organization's keys; revoking the active one mints its replacement in the same transaction.</summary>
    /// <param name="organizationId">The organization the key belongs to.</param>
    /// <param name="keyId">The key.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the revocation did.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.AdminCredentialsWrite" /> over the deployment.</exception>
    public async Task<OrganizationSigningKeyRevocation> RevokeAsync(
        Guid organizationId,
        Guid keyId,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminCredentialsWrite);

        var at = this.timeProvider.GetUtcNow();
        var revocation = await this.keys.RevokeAsync(organizationId, keyId, at, cancellationToken);

        if (revocation.Outcome == OrganizationSigningKeyRevocationOutcome.Revoked)
        {
            await this.RecordAsync(
                OrganizationSigningKeyAct.Revoked,
                organizationId,
                keyId,
                revocation.ReplacementKeyId,
                at,
                cancellationToken);
        }

        return revocation;
    }

    private Task RecordAsync(
        OrganizationSigningKeyAct act,
        Guid organizationId,
        Guid? keyId,
        Guid? mintedKeyId,
        DateTimeOffset at,
        CancellationToken cancellationToken) =>
        this.auditor.RecordAsync(
            new OrganizationSigningKeyChange(
                act,
                organizationId,
                keyId,
                mintedKeyId,
                this.authorization.PrincipalIdentity ?? AuthorizedPrincipal.ProcessIdentityName,
                at),
            cancellationToken);
}
