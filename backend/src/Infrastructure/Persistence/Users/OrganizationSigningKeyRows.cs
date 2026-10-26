// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Security.SigningKeys;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>The one statement that stores a freshly minted key as its organization's active key.</summary>
/// <remarks>
/// Shared by the organization's creation and by every rotation and revocation, because all of them have to lose the
/// same way to a key another replica stored first: the insert names the partial unique index as its arbiter and does
/// nothing on a conflict, so the caller learns it lost from the row count rather than from a failed transaction.
/// </remarks>
[RequiresIntegrationCoverage]
internal static class OrganizationSigningKeyRows
{
    /// <summary>Stores a key as the organization's active key unless it already holds one.</summary>
    /// <param name="dbContext">The context whose transaction, if any, the insert joins.</param>
    /// <param name="organizationId">The organization.</param>
    /// <param name="key">The minted key.</param>
    /// <param name="createdAt">When the key was minted.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><c>1</c> when the key was stored, <c>0</c> when the organization already held an active key.</returns>
    internal static Task<int> InsertActiveAsync(
        MailFathomDbContext dbContext,
        Guid organizationId,
        MintedOrganizationSigningKey key,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        var keyId = key.KeyId;
        var publicKey = key.PublicKey;
        var sealedPrivateKey = key.SealedPrivateKey.Ciphertext.ToArray();
        var dataEncryptionKeyId = key.SealedPrivateKey.KeyId;

        return dbContext.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO organization_signing_keys
                 ("Id", "OrganizationId", "PublicKey", "SealedPrivateKey", "DataEncryptionKeyId", "CreatedAt")
             VALUES ({keyId}, {organizationId}, {publicKey}, {sealedPrivateKey}, {dataEncryptionKeyId}, {createdAt})
             ON CONFLICT ("OrganizationId") WHERE "RetiredAt" IS NULL AND "RevokedAt" IS NULL DO NOTHING
             """,
            cancellationToken);
    }
}
