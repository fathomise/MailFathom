// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Access.Organizations.SigningKeys;

/// <summary>One page of an organization's signing keys, beside the name an operator reads the organization by.</summary>
/// <param name="OrganizationId">The organization.</param>
/// <param name="DisplayName">Its current display name, read through the organization because a key never stores one.</param>
/// <param name="Keys">The page's keys in identifier order, which is the order they were minted in.</param>
/// <param name="ContinuesAfter">The key the following page continues after, or <see langword="null" /> when this page is the last.</param>
/// <remarks>
/// Paged rather than cut at a count: a key is minted with every rotation and every revocation, an interval may be as
/// short as a day, and an administrator revoking an old key that leaked while it was active has to be able to find it.
/// </remarks>
public sealed record OrganizationSigningKeyListing(
    Guid OrganizationId,
    string DisplayName,
    IReadOnlyList<OrganizationSigningKey> Keys,
    Guid? ContinuesAfter);
