// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Paging;

namespace MailFathom.Application.Access.Organizations.SigningKeys;

/// <summary>Where every organization's signing keys are kept, minted, retired, and revoked.</summary>
/// <remarks>
/// <para>
/// The adapter generates the key pair and seals the private half under the deployment's data-encryption key ring, so no
/// key material crosses this port in either direction: what leaves it is identifiers and instants. That is what keeps
/// the material out of every log, endpoint, and command output above it — there is nothing here to print.
/// </para>
/// <para>
/// At most one key per organization is active, and the database enforces it rather than a read before the write. Two
/// replicas minting or rotating at once therefore leave one active key; the one that lost answers
/// <see cref="OrganizationSigningKeyReplacementOutcome.AlreadyCurrent" /> and names the winner's key.
/// </para>
/// </remarks>
public interface IOrganizationSigningKeyStore
{
    /// <summary>Reads one page of an organization's keys, in identifier order.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="query">The page to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page beside the organization's display name, or <see langword="null" /> when the deployment holds no such organization.</returns>
    Task<OrganizationSigningKeyListing?> ReadAsync(
        Guid organizationId,
        AdministrativeListingQuery query,
        CancellationToken cancellationToken);

    /// <summary>Finds organizations with no active key, or with one minted at or before an instant.</summary>
    /// <param name="activeKeyCreatedAtOrBefore">The instant an active key must be younger than to be kept.</param>
    /// <param name="limit">The most organizations to return.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The organizations' identifiers, in identifier order.</returns>
    Task<IReadOnlyList<Guid>> FindOrganizationsDueAsync(
        DateTimeOffset activeKeyCreatedAtOrBefore,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Mints a fresh active key for an organization unless it holds one minted after an instant, retiring the one it replaces.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="replaceKeyCreatedAtOrBefore">An active key minted at or before this instant is replaced; a younger one is kept. Passing the current instant replaces whatever is active.</param>
    /// <param name="at">The instant the new key is minted and the old one retired at.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the attempt did.</returns>
    /// <remarks>The new key and the retirement of the old one, including the deletion of its private half, are one transaction.</remarks>
    Task<OrganizationSigningKeyReplacement> ReplaceActiveKeyAsync(
        Guid organizationId,
        DateTimeOffset replaceKeyCreatedAtOrBefore,
        DateTimeOffset at,
        CancellationToken cancellationToken);

    /// <summary>Revokes one of an organization's keys, minting a replacement in the same transaction when it was the active one.</summary>
    /// <param name="organizationId">The organization the key belongs to.</param>
    /// <param name="keyId">The key.</param>
    /// <param name="at">The instant the revocation takes effect.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the revocation did.</returns>
    /// <remarks>The private half is deleted with the revocation. A replacement is minted only where a key ring lets one be sealed; the revocation stands either way.</remarks>
    Task<OrganizationSigningKeyRevocation> RevokeAsync(
        Guid organizationId,
        Guid keyId,
        DateTimeOffset at,
        CancellationToken cancellationToken);
}
