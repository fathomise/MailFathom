// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Contacts.Members;

/// <summary>Where one member sits in their organization, as a colleague reads it.</summary>
/// <param name="Managers">The managers above the member, from the top of the organization down to the member's own, each with their presence; empty for a member who names nobody.</param>
/// <param name="DirectReports">The members naming the member as their manager, ordered by name, each with their presence.</param>
/// <param name="Reader">The reader, who may stand in either list like any other member and is nobody's colleague to themselves.</param>
/// <remarks>
/// Derived from the one manager each member names whenever it is read, and held nowhere. A member is in either list only
/// while the directory lists them, so the chain ends below a manager who has become a service account.
/// </remarks>
public sealed record ColleagueStructure(
    IReadOnlyList<Colleague> Managers,
    IReadOnlyList<Colleague> DirectReports,
    UserId Reader);
