// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access;

/// <summary>Gives a user the row the mail graph hangs on, and keeps the label, the title, the type, and the manager on it the ones an administrator last wrote.</summary>
/// <remarks>
/// <para>
/// The envelope is all this writes: the row exists because <c>mailbox_accounts.UserId</c> is a foreign key and the
/// integrity of the mail graph is relational, and the document column stays the empty object it was provisioned with
/// until a write to the user's record fills it.
/// </para>
/// <para>
/// All five operations are idempotent, because two writers may reach one row at once and the one that lost has to be
/// answered with what the deployment holds rather than with a failure. Provisioning a user the deployment already
/// holds writes nothing, and relabelling one already carrying the label, or giving one the title, the type, or the
/// manager they already carry, leaves them as they were.
/// </para>
/// </remarks>
public interface IUserProvisioning
{
    /// <summary>Records a user this deployment did not hold, under the identifier they are declared with.</summary>
    /// <param name="user">The identity the user is declared under, which every mail account of theirs will name.</param>
    /// <param name="displayName">The label the user is told apart by.</param>
    /// <param name="organizationId">The organization the user is recorded into, or <see langword="null" /> to record them in none.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see cref="UserProvisioningResult.Provisioned" /> when the deployment holds this user once the write has run, or why it holds no row for them.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody, <paramref name="displayName" /> is <see langword="null" />, empty, or white space, or <paramref name="organizationId" /> is empty.</exception>
    /// <remarks>
    /// <para>
    /// The answer is what the deployment holds afterwards rather than whether this call was the one that wrote it, so
    /// a replica that lost the race to an identical declaration is told the same thing as the one that won. The one
    /// refusal is the outcome a caller has to act on: the organization has to stand when the row is written. The label
    /// is never a reason to refuse, because two people may carry one name and nothing resolves a user by it.
    /// </para>
    /// <para>
    /// The user enters the organization in the statement that records them rather than in a move afterwards, so an
    /// administrator scoped to that organization never records somebody who stands, even for a moment, where only the
    /// deployment's scope reaches them.
    /// </para>
    /// </remarks>
    Task<UserProvisioningResult> ProvisionAsync(
        UserId user,
        string displayName,
        Guid? organizationId,
        CancellationToken cancellationToken);

    /// <summary>Puts the label a declaration now carries onto the row of a user this deployment already holds.</summary>
    /// <param name="user">The user whose row is relabelled.</param>
    /// <param name="displayName">The label the user is now declared under.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when the row carries the label once the write has run, <see langword="false" /> when this deployment holds nobody under that identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody, or <paramref name="displayName" /> is <see langword="null" />, empty, or white space.</exception>
    /// <remarks>
    /// <para>
    /// A label is what an administrator reads a roster by rather than anything an account hangs on, so an administrator
    /// renaming a user over the endpoint is the whole of what renames them, and the new label lasts — no configuration
    /// source names a user, so no start puts an earlier one back. The identifier is the opposite case and is refused
    /// rather than followed, because changing it would orphan every mail account and every stored message recorded
    /// under the old one.
    /// </para>
    /// <para>
    /// The answer is read the way <see cref="ProvisionAsync" />'s is, and for the same race: a user erased between a
    /// caller reading them and this statement reaching the table is reported as one the deployment does not hold.
    /// </para>
    /// </remarks>
    Task<bool> RelabelAsync(UserId user, string displayName, CancellationToken cancellationToken);

    /// <summary>Puts the title the user's colleagues read them by onto the row of a user this deployment already holds.</summary>
    /// <param name="user">The user whose row is written.</param>
    /// <param name="jobTitle">The title, at most <see cref="UserRecord.MaximumJobTitleLength" /> characters, or <see langword="null" /> to state none.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when the row carries the title once the write has run, <see langword="false" /> when this deployment holds nobody under that identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody, or <paramref name="jobTitle" /> is empty, white space, or longer than the column.</exception>
    /// <remarks>Answered as <see cref="RelabelAsync" /> is, and for the same race.</remarks>
    Task<bool> SetJobTitleAsync(UserId user, string? jobTitle, CancellationToken cancellationToken);

    /// <summary>Puts whether the user is a person or a technical account onto the row of a user this deployment already holds.</summary>
    /// <param name="user">The user whose row is written.</param>
    /// <param name="type">The type the user carries from now on.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when the row carries the type once the write has run, <see langword="false" /> when this deployment holds nobody under that identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="type" /> names no declared value.</exception>
    /// <remarks>Answered as <see cref="RelabelAsync" /> is, and for the same race.</remarks>
    Task<bool> SetTypeAsync(UserId user, UserType type, CancellationToken cancellationToken);

    /// <summary>Names the member of the same organization a user this deployment holds reports to, or names nobody.</summary>
    /// <param name="user">The user whose row is written.</param>
    /// <param name="manager">The manager from now on, or <see langword="null" /> to name nobody.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>
    /// <see cref="ManagerAssignmentResult.Assigned" /> when the row names the manager once the write has run, or the
    /// rule the write would have broken: the user named as their own manager, a manager who is not a person of the
    /// user's organization, or a manager who already reports to the user. Naming nobody refuses nothing but an unknown user.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> or <paramref name="manager" /> names nobody.</exception>
    /// <remarks>
    /// Unlike the other writes here, this one is judged against other rows, so it is judged and written under locks that
    /// make two concurrent writes in one organization see each other; naming the manager already named is still a write
    /// that leaves the row as it was.
    /// </remarks>
    Task<ManagerAssignmentResult> SetManagerAsync(UserId user, UserId? manager, CancellationToken cancellationToken);
}
