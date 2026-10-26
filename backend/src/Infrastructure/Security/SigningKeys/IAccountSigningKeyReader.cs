// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Infrastructure.Security.SigningKeys;

/// <summary>Reads the key a mail account's organization signs with now.</summary>
/// <remarks>
/// It stays inside this boundary because it hands out a sealed private half, which the application ports deliberately
/// never carry. One read answers both the organization and its key, so a key rotated or an account moved between the
/// two reads cannot pair an account with another organization's key.
/// </remarks>
internal interface IAccountSigningKeyReader
{
    /// <summary>Reads the account's organization and that organization's active key, as they stand now.</summary>
    /// <param name="account">The sending account.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The reading; an account the deployment holds no record of reads as one in no organization.</returns>
    Task<AccountSigningKeyReading> ReadAsync(MailAccountId account, CancellationToken cancellationToken);
}
