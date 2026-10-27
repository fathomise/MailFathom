// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access.Organizations.SigningKeys;
using MailFathom.Application.Paging;
using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Security.SigningKeys;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>Keeps the organizations' signing keys in PostgreSQL, with every private half sealed under the key ring.</summary>
/// <remarks>
/// <para>
/// A key is generated and sealed before its transaction opens, because sealing resolves the ring's active key through
/// a secret reference and nothing remote belongs inside a transaction. A key generated for an attempt that then finds
/// nothing to do is discarded unsaved; it never existed anywhere but this process.
/// </para>
/// <para>
/// Retiring or revoking a key and deleting its private half are one statement, and storing the key that replaces it is
/// the next statement of the same transaction. The partial unique index on active keys is what decides a race: the
/// attempt whose insert finds another active key rolls back, leaving the winner's key and its own retirement undone.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class PersistedOrganizationSigningKeys(
    MailFathomDbContext dbContext,
    OrganizationSigningKeyMinter minter)
    : IOrganizationSigningKeyStore
{
    /// <inheritdoc />
    public async Task<OrganizationSigningKeyListing?> ReadAsync(
        Guid organizationId,
        AdministrativeListingQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var displayName = await dbContext.Organizations
            .AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.DisplayName)
            .SingleOrDefaultAsync(cancellationToken);

        if (displayName is null)
        {
            return null;
        }

        var organizationKeys = dbContext.OrganizationSigningKeys
            .AsNoTracking()
            .Where(key => key.OrganizationId == organizationId);

        // Compared by PostgreSQL as a `uuid`, the order the keys were minted in and the order the page is taken in.
        if (query.After is { } after)
        {
            organizationKeys = organizationKeys.Where(key => key.Id > after);
        }

        var keys = await organizationKeys
            .OrderBy(key => key.Id)
            .Take(query.PageSize + 1)
            .Select(key => new OrganizationSigningKey(key.Id, key.CreatedAt, key.RetiredAt, key.RevokedAt))
            .ToArrayAsync(cancellationToken);

        var page = query.PageOf(keys, key => key.KeyId);

        return new OrganizationSigningKeyListing(organizationId, displayName, page.Entries, page.ContinuesAfter);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> FindOrganizationsDueAsync(
        DateTimeOffset activeKeyCreatedAtOrBefore,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return await dbContext.Organizations
            .AsNoTracking()
            .Where(organization => !dbContext.OrganizationSigningKeys.Any(key =>
                key.OrganizationId == organization.Id
                && key.RetiredAt == null
                && key.RevokedAt == null
                && key.CreatedAt > activeKeyCreatedAtOrBefore))
            .OrderBy(organization => organization.Id)
            .Select(organization => organization.Id)
            .Take(limit)
            .ToArrayAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OrganizationSigningKeyReplacement> ReplaceActiveKeyAsync(
        Guid organizationId,
        DateTimeOffset replaceKeyCreatedAtOrBefore,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        if (!await this.OrganizationExistsAsync(organizationId, cancellationToken))
        {
            return OrganizationSigningKeyReplacement.Of(OrganizationSigningKeyReplacementOutcome.UnknownOrganization);
        }

        if (!minter.CanMint)
        {
            return OrganizationSigningKeyReplacement.Of(OrganizationSigningKeyReplacementOutcome.NoKeyRing);
        }

        var minted = await minter.MintAsync(Guid.CreateVersion7(at), cancellationToken);

        await using var replacement = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var retired = await dbContext.Database
            .SqlQuery<Guid>(
                $"""
                 UPDATE organization_signing_keys
                 SET "RetiredAt" = {at}, "SealedPrivateKey" = NULL, "DataEncryptionKeyId" = NULL
                 WHERE "OrganizationId" = {organizationId}
                   AND "RetiredAt" IS NULL AND "RevokedAt" IS NULL
                   AND "CreatedAt" <= {replaceKeyCreatedAtOrBefore}
                 RETURNING "Id" AS "Value"
                 """)
            .ToArrayAsync(cancellationToken);

        if (await this.TryInsertActiveAsync(organizationId, minted, at, cancellationToken) is not { } stored)
        {
            return OrganizationSigningKeyReplacement.Of(OrganizationSigningKeyReplacementOutcome.UnknownOrganization);
        }

        if (!stored)
        {
            await replacement.RollbackAsync(cancellationToken);

            return new OrganizationSigningKeyReplacement(
                OrganizationSigningKeyReplacementOutcome.AlreadyCurrent,
                await this.FindActiveKeyIdAsync(organizationId, cancellationToken),
                RetiredKeyId: null);
        }

        await replacement.CommitAsync(cancellationToken);

        return new OrganizationSigningKeyReplacement(
            OrganizationSigningKeyReplacementOutcome.Replaced,
            minted.KeyId,
            retired.Length == 0 ? null : retired[0]);
    }

    /// <inheritdoc />
    public async Task<OrganizationSigningKeyRevocation> RevokeAsync(
        Guid organizationId,
        Guid keyId,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        var minted = await this.TryMintReplacementAsync(organizationId, keyId, at, cancellationToken);

        await using var revocation = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var revoked = await dbContext.Database
            .SqlQuery<bool>(
                $"""
                 UPDATE organization_signing_keys
                 SET "RevokedAt" = {at}, "SealedPrivateKey" = NULL, "DataEncryptionKeyId" = NULL
                 WHERE "Id" = {keyId} AND "OrganizationId" = {organizationId} AND "RevokedAt" IS NULL
                 RETURNING "RetiredAt" IS NULL AS "Value"
                 """)
            .ToArrayAsync(cancellationToken);

        if (revoked.Length == 0)
        {
            var known = await dbContext.OrganizationSigningKeys
                .AnyAsync(key => key.Id == keyId && key.OrganizationId == organizationId, cancellationToken);

            return OrganizationSigningKeyRevocation.Of(
                known ? OrganizationSigningKeyRevocationOutcome.AlreadyRevoked : OrganizationSigningKeyRevocationOutcome.UnknownKey);
        }

        var wasActive = revoked[0];
        Guid? replacementKeyId = null;

        if (wasActive && minted is not null)
        {
            // A concurrent mint for an organization caught between keys may have stored one first; that one is the
            // replacement then, and the revocation stands either way.
            switch (await this.TryInsertActiveAsync(organizationId, minted, at, cancellationToken))
            {
                case true:
                    replacementKeyId = minted.KeyId;
                    break;

                case false:
                    replacementKeyId = await this.FindActiveKeyIdAsync(organizationId, cancellationToken);
                    break;

                default:
                    // The organization was deleted under the revocation, and its keys with it.
                    return OrganizationSigningKeyRevocation.Of(OrganizationSigningKeyRevocationOutcome.UnknownKey);
            }
        }

        await revocation.CommitAsync(cancellationToken);

        return new OrganizationSigningKeyRevocation(OrganizationSigningKeyRevocationOutcome.Revoked, wasActive, replacementKeyId);
    }

    /// <summary>Stores a key as the organization's active key.</summary>
    /// <returns><see langword="true" /> when it was stored, <see langword="false" /> when the organization already held an active key, and <see langword="null" /> when the organization no longer exists.</returns>
    private async Task<bool?> TryInsertActiveAsync(
        Guid organizationId,
        MintedOrganizationSigningKey minted,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        try
        {
            return await OrganizationSigningKeyRows.InsertActiveAsync(dbContext, organizationId, minted, at, cancellationToken) == 1;
        }
        catch (PostgresException violation) when (violation.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            // Deleted between the existence check and the insert; the transaction is spent and rolls back on disposal.
            return null;
        }
    }

    /// <summary>Mints the key that will replace <paramref name="keyId" />, when it is the organization's active key and a key can be sealed.</summary>
    /// <returns>The replacement, or <see langword="null" /> when the key needs none or none can be sealed now.</returns>
    /// <remarks>
    /// A revocation never waits on the key ring: withdrawing trust from a leaked key matters more than replacing it, so a
    /// ring whose active key cannot be resolved leaves the organization without an active key, and the hourly pass mints
    /// one once the ring answers again — reporting the failure itself as it does every failed pass.
    /// </remarks>
    private async Task<MintedOrganizationSigningKey?> TryMintReplacementAsync(
        Guid organizationId,
        Guid keyId,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        if (!minter.CanMint || !await this.IsActiveKeyAsync(organizationId, keyId, cancellationToken))
        {
            return null;
        }

        try
        {
            return await minter.MintAsync(Guid.CreateVersion7(at), cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private Task<bool> IsActiveKeyAsync(Guid organizationId, Guid keyId, CancellationToken cancellationToken) =>
        dbContext.OrganizationSigningKeys.AnyAsync(
            key => key.Id == keyId && key.OrganizationId == organizationId && key.RetiredAt == null && key.RevokedAt == null,
            cancellationToken);

    private Task<bool> OrganizationExistsAsync(Guid organizationId, CancellationToken cancellationToken) =>
        dbContext.Organizations.AnyAsync(organization => organization.Id == organizationId, cancellationToken);

    private Task<Guid?> FindActiveKeyIdAsync(Guid organizationId, CancellationToken cancellationToken) =>
        dbContext.OrganizationSigningKeys
            .AsNoTracking()
            .Where(key => key.OrganizationId == organizationId && key.RetiredAt == null && key.RevokedAt == null)
            .Select(key => (Guid?)key.Id)
            .SingleOrDefaultAsync(cancellationToken);
}
