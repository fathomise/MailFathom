// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access;

/// <summary>Takes one user off this deployment, with everything it recorded for them.</summary>
/// <remarks>
/// <para>
/// The counterpart of <see cref="IUserProvisioning" /> and deliberately a port of its own, because the two are
/// not one capability with a flag. Provisioning is idempotent because two writers may reach one row at once and the
/// loser is answered with what the deployment holds; this runs when a person asked for it, once, and what it removes
/// cannot be written back. A deployment that never calls it goes on holding the user, and every user a deployment
/// holds is served.
/// </para>
/// <para>
/// It is the whole of the person rather than the user row alone: their assignments, the drafts and recurring sends
/// they authored in any account, their own contact book, and their credentials and sessions. It is never a mail
/// account. The mail graph belongs to the account, so an account this user alone was assigned stays with its mail,
/// assigned to nobody and out of every flow, until somebody is assigned it again or it is erased on purpose.
/// </para>
/// </remarks>
public interface IUserErasure
{
    /// <summary>Erases one user and everything this deployment recorded for them, leaving every mail account in place.</summary>
    /// <param name="user">The user to remove.</param>
    /// <param name="cancellationToken">Cancels the erasure before it commits, leaving the deployment unchanged.</param>
    /// <returns>Whether a user record was there to remove; nothing removed is the repeat of an erasure that already ran.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <remarks>
    /// The whole of it commits or none of it does, because a partial erasure is a data-subject request answered with
    /// half of somebody's record still stored. An erasure that removed nothing is not a failure: what the caller asked
    /// for is that the deployment hold nothing for this user, and it does.
    /// </remarks>
    Task<bool> EraseAsync(UserId user, CancellationToken cancellationToken);
}
