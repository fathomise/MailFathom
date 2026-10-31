// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access;

/// <summary>Reads who stands above whom in an organization, from the one manager each member names.</summary>
/// <remarks>
/// Direct reports are never stored: they are every member naming somebody as their manager, so the only fact kept is
/// the upward edge, and every question about the structure is a walk along it.
/// </remarks>
public static class ReportingLine
{
    /// <summary>Reads the managers above one member, nearest first.</summary>
    /// <param name="member">The member whose managers are read.</param>
    /// <param name="managers">Each member's manager, keyed by the member; a member naming nobody is absent.</param>
    /// <returns>The member's manager, that manager's, and so on, ending at the first who names nobody, or before the first who would repeat.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="managers" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A repeat ends the walk rather than looping, so a line read half before and half after a concurrent write still
    /// answers. The writes refuse every cycle, which is why a repeat is never expected.
    /// </remarks>
    public static IReadOnlyList<UserId> ManagersAbove(UserId member, IReadOnlyDictionary<UserId, UserId> managers)
    {
        ArgumentNullException.ThrowIfNull(managers);

        var chain = new List<UserId>();
        var seen = new HashSet<UserId> { member };

        for (var current = member; managers.TryGetValue(current, out var manager) && seen.Add(manager); current = manager)
        {
            chain.Add(manager);
        }

        return chain;
    }

    /// <summary>Says whether naming one member's manager would close the line on itself.</summary>
    /// <param name="member">The member whose manager would be named.</param>
    /// <param name="manager">The manager that would be named.</param>
    /// <param name="managers">Each member's manager as the organization holds them before the write.</param>
    /// <returns><see langword="true" /> where <paramref name="manager" /> is <paramref name="member" /> or already reports to them, directly or through others.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="managers" /> is <see langword="null" />.</exception>
    public static bool WouldCycle(UserId member, UserId manager, IReadOnlyDictionary<UserId, UserId> managers) =>
        manager == member || ManagersAbove(manager, managers).Contains(member);
}
