// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence.Policies;

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>The people of one organization, as one reading of their rows.</summary>
/// <param name="OrganizationName">The name the organization is shown by.</param>
/// <param name="Policies">The settings policies governing every member's record, which are the deployment's and the organization's.</param>
/// <param name="Members">Every member whose type is a person, in identifier order.</param>
public sealed record OrganizationMembers(
    string OrganizationName,
    GoverningSettingsPolicies Policies,
    IReadOnlyList<OrganizationMember> Members);
