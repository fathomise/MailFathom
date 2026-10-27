// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail;
using MailFathom.Application.Mail.KnownAuthenticationProviders;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails.Authentication;

namespace MailFathom.Host.Configuration.Mail.Readers;

/// <summary>Reads which authentication results an account believes, from its own setting or from the known-provider list.</summary>
/// <remarks>
/// One instance serves one scope, which is one work unit, so a host's entry is read from the list once per work unit
/// rather than once per message, and the next work unit reads whatever the list holds by then.
/// </remarks>
internal sealed class ConfiguredTrustedAuthenticationAuthorityReader(
    MailSynchronizationOptions settings,
    IKnownAuthenticationProviderDirectory knownProviders)
    : ITrustedAuthenticationAuthorityReader
{
    private readonly Dictionary<string, TrustedAuthenticationAuthority> authoritiesByHost = new(StringComparer.Ordinal);

    /// <inheritdoc />
    /// <remarks>
    /// An account this snapshot no longer names answers with none rather than failing, for the reason the audit
    /// settings do: the extraction backfill runs over accounts a reload may have removed between one run and the
    /// next, and believing no header there is the honest answer as well as the safe one.
    /// </remarks>
    public async Task<TrustedAuthenticationAuthority> GetTrustedAuthorityAsync(
        MailAccountId accountId,
        CancellationToken cancellationToken)
    {
        if (settings.FindConfiguredAccount(accountId) is not { } account)
        {
            return TrustedAuthenticationAuthority.None;
        }

        if (account.TrustedAuthenticationServiceIdentifier is not null)
        {
            return account.CreateTrustedAuthority();
        }

        if (KnownAuthenticationProvider.TryNormalizeImapHost(account.Host) is not { } host)
        {
            return TrustedAuthenticationAuthority.None;
        }

        if (!this.authoritiesByHost.TryGetValue(host, out var authority))
        {
            authority = (await knownProviders.FindAsync(host, cancellationToken))?.Authority
                ?? TrustedAuthenticationAuthority.None;
            this.authoritiesByHost[host] = authority;
        }

        return authority;
    }
}
