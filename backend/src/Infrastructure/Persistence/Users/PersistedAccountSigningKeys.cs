// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.DataEncryption;
using MailFathom.Infrastructure.Security.SigningKeys;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>Reads a mail account's organization and that organization's active signing key in one statement.</summary>
/// <remarks>
/// The read joins no transaction: it is one statement, so the account's organization and the key it names are read
/// together, and nothing is held across the signing and the submission that follow it.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class PersistedAccountSigningKeys(MailFathomDbContext dbContext) : IAccountSigningKeyReader
{
    /// <inheritdoc />
    public async Task<AccountSigningKeyReading> ReadAsync(MailAccountId account, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(account.Value, out var accountId))
        {
            return AccountSigningKeyReading.OutsideEveryOrganization;
        }

        var found = await dbContext.MailAccountRecords
            .AsNoTracking()
            .Where(record => record.Id == accountId && record.OrganizationId != null)
            .Select(record => new
            {
                Key = dbContext.OrganizationSigningKeys
                    .Where(key => key.OrganizationId == record.OrganizationId
                        && key.RetiredAt == null
                        && key.RevokedAt == null
                        && key.SealedPrivateKey != null)
                    .Select(key => new { key.Id, key.SealedPrivateKey, key.DataEncryptionKeyId })
                    .SingleOrDefault(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        return found switch
        {
            null => AccountSigningKeyReading.OutsideEveryOrganization,
            { Key: { SealedPrivateKey: { } sealedPrivateKey, DataEncryptionKeyId: { } ringKeyId } key } =>
                AccountSigningKeyReading.Active(key.Id, new SealedValue(ringKeyId, sealedPrivateKey)),
            _ => AccountSigningKeyReading.WithoutActiveKey,
        };
    }
}
