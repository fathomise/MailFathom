// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails.Authentication;

namespace MailFathom.Application.Mail.KnownAuthenticationProviders;

/// <summary>Reads the deployment-wide list of providers whose receiving server an account believes by default.</summary>
/// <remarks>
/// Read from PostgreSQL on every lookup, so a change an operator made reaches the next extraction on every replica
/// without a cache to expire or a restart.
/// </remarks>
public interface IKnownAuthenticationProviderDirectory
{
    /// <summary>Finds the entry for the IMAP host an account synchronizes from.</summary>
    /// <param name="imapHost">The host in the form <see cref="KnownAuthenticationProvider.TryNormalizeImapHost" /> produces.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The entry, or <see langword="null" /> when the list names no provider for that host.</returns>
    Task<KnownAuthenticationProvider?> FindAsync(string imapHost, CancellationToken cancellationToken);
}
