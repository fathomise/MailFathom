// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Signals;
using MailFathom.Domain.Access;
using MailFathom.Domain.AutomaticReplies;

namespace MailFathom.Application.AutomaticReplies;

/// <summary>Reads and saves the signed-in user's own automatic reply.</summary>
/// <param name="authorization">Reports the grant the caller holds and the user it acts for.</param>
/// <param name="store">Holds the reply.</param>
/// <param name="assignments">Answers which accounts are the user's and which of them are shared.</param>
/// <param name="placements">Answers which organization the user is a member of.</param>
/// <param name="signals">Tells the user's own clients when they start or stop reading to their colleagues as out of office.</param>
/// <param name="time">Says which part of a period has begun.</param>
public sealed class OwnAutomaticReply(
    AccessAuthorization authorization,
    IAutomaticReplyStore store,
    IMailAccountAssignments assignments,
    IAdministrativeTargets placements,
    ClientSignals signals,
    TimeProvider time)
{
    /// <summary>Reads the signed-in user's automatic reply.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The reply, or <see langword="null" /> where they never set one.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.ProfileRead" />.</exception>
    public Task<StoredAutomaticReply?> ReadAsync(CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.ProfileRead);

        return store.ReadAsync(authorization.RequireUser(), cancellationToken);
    }

    /// <summary>Replaces the signed-in user's automatic reply, or says why it may not be saved.</summary>
    /// <param name="reply">The whole reply.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What became of the write, naming the account a refusal is about.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reply" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.ProfileWrite" /> or <see cref="MailFathomPermission.MailSend" />.</exception>
    /// <remarks>
    /// <para>
    /// The sending grant is asked beside the profile one, because what is saved here sends mail as the user's accounts
    /// for weeks without anybody present: a caller that may not send may not arrange to send later either.
    /// </para>
    /// <para>
    /// A shared mailbox is refused because nobody is away on its behalf: one of the people reading it being absent
    /// says nothing about whether the mailbox is answered. The accounts are checked when the reply is saved and again
    /// whenever mail arrives, so an account shared after the save stops being answered without anybody rewriting the
    /// reply.
    /// </para>
    /// <para>
    /// A save that changes whether, or until when, the user reads to their colleagues as out of office raises
    /// <see cref="ClientSignal.PresenceChanged" /> to the user's own clients alone; colleagues read it on their own
    /// refresh. A period beginning or ending with nobody saving anything raises nothing.
    /// </para>
    /// </remarks>
    public async Task<AutomaticReplyWrite> SaveAsync(AutomaticReply reply, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reply);

        authorization.RequirePermission(MailFathomPermission.ProfileWrite);
        authorization.RequirePermission(MailFathomPermission.MailSend);

        var user = authorization.RequireUser();

        if (await this.RefuseAsync(user, reply, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        var before = await store.ReadAsync(user, cancellationToken);

        if (await store.SaveAsync(user, reply, cancellationToken) is not { } saved)
        {
            return new AutomaticReplyWrite(AutomaticReplyWriteOutcome.UserUnknown);
        }

        await this.AnnounceIfSeenDifferentlyAsync(user, before?.Reply.Period, reply.Period, cancellationToken);

        return new AutomaticReplyWrite(AutomaticReplyWriteOutcome.Saved, saved);
    }

    private async Task AnnounceIfSeenDifferentlyAsync(
        UserId user,
        AutomaticReplyPeriod? before,
        AutomaticReplyPeriod after,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();

        if (before?.OutOfOfficeUntil(now) == after.OutOfOfficeUntil(now)
            || (await placements.PlaceUserAsync(user, cancellationToken)).Organization is null)
        {
            return;
        }

        signals.Publish(ClientSignal.PresenceChanged(user));
    }

    private async Task<AutomaticReplyWrite?> RefuseAsync(
        UserId user,
        AutomaticReply reply,
        CancellationToken cancellationToken)
    {
        var assigned = (await assignments.ReadAccountsAssignedToAsync(user, cancellationToken)).ToHashSet();

        if (reply.Accounts.Where(account => !assigned.Contains(account)).ToArray() is [var foreign, ..])
        {
            return new AutomaticReplyWrite(AutomaticReplyWriteOutcome.AccountNotAssigned, Account: foreign);
        }

        var shared = await assignments.ReadSharedAmongAsync(reply.Accounts, cancellationToken);

        if (reply.Accounts.Where(shared.Contains).ToArray() is [var sharedAccount, ..])
        {
            return new AutomaticReplyWrite(AutomaticReplyWriteOutcome.AccountShared, Account: sharedAccount);
        }

        if (reply.InsideText is not null
            && (await placements.PlaceUserAsync(user, cancellationToken)).Organization is null)
        {
            return new AutomaticReplyWrite(AutomaticReplyWriteOutcome.NoOrganization);
        }

        return null;
    }
}
