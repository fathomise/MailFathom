// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Mail.Mutations.Destinations;

/// <summary>Resolves a folder the account maps but no run synchronizes, against its server, at the moment it is needed as a destination.</summary>
/// <remarks>
/// A folder the account only maps is never scheduled by a run, so nothing would ever bind it: it is resolved here,
/// through the same resolver, the same binding record, and the same mapping-change audit a mirrored folder uses. A
/// server that has since renamed the folder is followed exactly as it is for a mirrored folder — the binding is
/// replaced by the next generation, with no checkpoint to invalidate, because a folder nothing mirrors has none.
/// </remarks>
public sealed class OnDemandMailboxDestinationResolver
{
    private readonly MailFolderResolver folderResolver;
    private readonly IMailTransportSecurityPolicyReader transportSecurityPolicies;

    /// <summary>Initializes the resolver from what an on-demand resolution reaches the server through.</summary>
    /// <param name="folderResolver">Resolves a mapped folder against what its server advertises, and records the binding.</param>
    /// <param name="transportSecurityPolicies">Supplies the connection and authentication policy the resolution obeys.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public OnDemandMailboxDestinationResolver(
        MailFolderResolver folderResolver,
        IMailTransportSecurityPolicyReader transportSecurityPolicies)
    {
        ArgumentNullException.ThrowIfNull(folderResolver);
        ArgumentNullException.ThrowIfNull(transportSecurityPolicies);

        this.folderResolver = folderResolver;
        this.transportSecurityPolicies = transportSecurityPolicies;
    }

    /// <summary>Resolves one mapped folder against what its server advertises.</summary>
    /// <param name="account">The account.</param>
    /// <param name="mapping">The folder's mapping.</param>
    /// <param name="cancellationToken">Cancels the listing and the write that records a new binding.</param>
    /// <returns>The folder, or why there is none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="mapping" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A creation the server refused is reported as an unadvertised folder rather than raised, for the reason every
    /// refusal here is a result: the folder is not there either way, and letting it out would end a pass over one
    /// destination and end the next pass the same way, forever.
    /// </remarks>
    public async Task<MailboxDestinationResolution> ResolveAsync(
        MailAccountId account,
        MailFolderMapping mapping,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        var transportSecurityPolicy = this.transportSecurityPolicies.GetPolicy(account);

        MailFolderResolutionResult result;

        try
        {
            result = await this.folderResolver.ResolveAsync(
                account,
                mapping,
                transportSecurityPolicy,
                cancellationToken);
        }
        catch (RemoteFolderCreationRefusedException)
        {
            return MailboxDestinationResolution.NotAdvertised();
        }

        return result switch
        {
            { Outcome: MailFolderResolutionOutcome.Resolved, Resolution: { } binding } =>
                MailboxDestinationResolution.Resolved(new MailboxDestination(binding, IsMirrored: false, mapping.SpecialUse)),
            { Outcome: MailFolderResolutionOutcome.AdvertisedFoldersAreAmbiguous } =>
                MailboxDestinationResolution.Ambiguous(),
            _ => MailboxDestinationResolution.NotAdvertised(),
        };
    }
}
