// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Paging;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails.Authentication;

namespace MailFathom.Application.Mail.KnownAuthenticationProviders;

/// <summary>What a deployment administrator does to the known authentication providers: list, add, and remove entries.</summary>
/// <remarks>
/// <para>
/// Adding and removing are <see cref="MailFathomPermission.AdminConfigurationWrite" /> held over the deployment, because
/// an entry decides which header every account on that host believes, across every organization. Reading is
/// <see cref="MailFathomPermission.AdminRead" /> over the deployment as well. An organization-scoped administrator can do
/// neither.
/// </para>
/// <para>
/// Each change is written to the list's append-only change record with the caller's identity, which names the user and
/// the credential. A change reaches the next extraction on every replica, because the list is read where an account
/// believes no server of its own and nothing caches it beyond one work unit. It judges new mail only; a re-derivation
/// applies it to mail already stored.
/// </para>
/// </remarks>
public sealed class KnownAuthenticationProviderAdministration
{
    private readonly AccessAuthorization authorization;
    private readonly IKnownAuthenticationProviderStore providers;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the administration over one deployment's known authentication providers.</summary>
    /// <param name="authorization">What each act is admitted by.</param>
    /// <param name="providers">Where the list is kept.</param>
    /// <param name="timeProvider">The clock a change is stamped with.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public KnownAuthenticationProviderAdministration(
        AccessAuthorization authorization,
        IKnownAuthenticationProviderStore providers,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.authorization = authorization;
        this.providers = providers;
        this.timeProvider = timeProvider;
    }

    /// <summary>Reads one page of the list, in the order the entries were added.</summary>
    /// <param name="query">The page to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.AdminRead" /> over the deployment.</exception>
    public Task<KnownAuthenticationProviderListing> ReadAsync(
        AdministrativeListingQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        this.authorization.RequirePermission(MailFathomPermission.AdminRead);

        return this.providers.ReadAsync(query, cancellationToken);
    }

    /// <summary>Adds an entry.</summary>
    /// <param name="provider">The entry.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when it was added; <see langword="false" /> when the list already names a provider for its host.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.AdminConfigurationWrite" /> over the deployment.</exception>
    public Task<bool> AddAsync(KnownAuthenticationProvider provider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);

        this.authorization.RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        return this.providers.AddAsync(provider, this.ChangedBy, this.timeProvider.GetUtcNow(), cancellationToken);
    }

    /// <summary>Removes the entry for one host.</summary>
    /// <param name="imapHost">The host, in its matching form.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when an entry was removed; <see langword="false" /> when the list named none for that host.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.AdminConfigurationWrite" /> over the deployment.</exception>
    public Task<bool> RemoveAsync(string imapHost, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imapHost);

        this.authorization.RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        return this.providers.RemoveAsync(imapHost, this.ChangedBy, this.timeProvider.GetUtcNow(), cancellationToken);
    }

    private string ChangedBy => this.authorization.PrincipalIdentity ?? AuthorizedPrincipal.ProcessIdentityName;
}
