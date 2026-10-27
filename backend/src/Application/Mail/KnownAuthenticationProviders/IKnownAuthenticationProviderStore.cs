// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Paging;
using MailFathom.Domain.Emails.Authentication;

namespace MailFathom.Application.Mail.KnownAuthenticationProviders;

/// <summary>Keeps the deployment-wide list of known authentication providers and its append-only change record.</summary>
/// <remarks>
/// Every addition and every removal writes its change record in the same transaction as the entry itself, so the record
/// names every entry the list has ever held, including one a removal left nothing else of.
/// </remarks>
public interface IKnownAuthenticationProviderStore
{
    /// <summary>Reads one page of the list, in the order the entries were added.</summary>
    /// <param name="query">The page to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page.</returns>
    Task<KnownAuthenticationProviderListing> ReadAsync(AdministrativeListingQuery query, CancellationToken cancellationToken);

    /// <summary>Adds an entry, unless the list already names a provider for its host.</summary>
    /// <param name="provider">The entry.</param>
    /// <param name="changedBy">Who added it: the caller's identity, which names the user and the credential.</param>
    /// <param name="at">When it was added.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when the entry was added; <see langword="false" /> when its host already had one, which a concurrent addition may also be the reason for.</returns>
    Task<bool> AddAsync(
        KnownAuthenticationProvider provider,
        string changedBy,
        DateTimeOffset at,
        CancellationToken cancellationToken);

    /// <summary>Removes the entry for one host.</summary>
    /// <param name="imapHost">The host, in its matching form.</param>
    /// <param name="changedBy">Who removed it.</param>
    /// <param name="at">When it was removed.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when an entry was removed; <see langword="false" /> when the list named none for that host.</returns>
    Task<bool> RemoveAsync(
        string imapHost,
        string changedBy,
        DateTimeOffset at,
        CancellationToken cancellationToken);
}
