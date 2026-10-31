// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>One person of an organization, as their row and the accounts that are theirs alone hold them.</summary>
/// <param name="User">The member's user.</param>
/// <param name="DisplayName">The label the member's user carries.</param>
/// <param name="JobTitle">The title an administrator gave the member, or <see langword="null" /> for none.</param>
/// <param name="Json">The member's own record as the row holds it, or <see langword="null" /> where it is past what this build binds a record from.</param>
/// <param name="Addresses">The addresses of the mail accounts in the same organization assigned to the member and to nobody else, in the ordinal order of the accounts' identifiers.</param>
public sealed record OrganizationMember(
    UserId User,
    string DisplayName,
    string? JobTitle,
    string? Json,
    IReadOnlyList<string> Addresses);
