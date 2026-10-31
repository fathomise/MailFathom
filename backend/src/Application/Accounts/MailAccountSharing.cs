// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Accounts;

/// <summary>Says which mail accounts are shared: assigned to more than one user, so others read what arrives there.</summary>
/// <remarks>
/// Read from the assignment relation and never from the mail, so it answers the same before the first message is
/// stored as after. It reports a fact about each account and nothing about who the other users are: a caller learns
/// that somebody else reads the mailbox, which it may know, and never whom.
/// </remarks>
public static class MailAccountSharing
{
    /// <summary>Reads which of the given accounts are assigned to more than one user.</summary>
    /// <param name="assignments">The assignment relation.</param>
    /// <param name="accounts">The accounts asked about.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The accounts among those given that more than one user is assigned.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assignments" /> or <paramref name="accounts" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// One read per account, bounded by how many accounts one user may be assigned. A caller listing many more would
    /// want the relation counted in one statement instead.
    /// </remarks>
    public static async Task<IReadOnlySet<MailAccountId>> ReadSharedAmongAsync(
        this IMailAccountAssignments assignments,
        IEnumerable<MailAccountId> accounts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(accounts);

        var shared = new HashSet<MailAccountId>();

        foreach (var account in accounts)
        {
            if ((await assignments.ReadUsersAssignedToAsync(account, cancellationToken)).Count > 1)
            {
                shared.Add(account);
            }
        }

        return shared;
    }
}
