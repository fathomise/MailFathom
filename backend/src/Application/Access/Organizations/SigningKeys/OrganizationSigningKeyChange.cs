// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Access.Organizations.SigningKeys;

/// <summary>One administrative act on an organization's signing keys, as the audit record carries it.</summary>
/// <param name="Act">What was done.</param>
/// <param name="OrganizationId">The organization whose key it was.</param>
/// <param name="KeyId">The key the act retired or revoked, or <see langword="null" /> for a rotation of an organization that had no active key.</param>
/// <param name="MintedKeyId">The key the act minted to sign with from then on, or <see langword="null" /> when it minted none.</param>
/// <param name="ActingAdministrator">The identity the request was admitted as.</param>
/// <param name="OccurredAt">When the act took effect.</param>
/// <remarks>Identifiers only: no key material, and no organization name, which a listing resolves for whoever may read one.</remarks>
public sealed record OrganizationSigningKeyChange(
    OrganizationSigningKeyAct Act,
    Guid OrganizationId,
    Guid? KeyId,
    Guid? MintedKeyId,
    string ActingAdministrator,
    DateTimeOffset OccurredAt);
