// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.AutomaticReplies;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;

namespace MailFathom.Application.AutomaticReplies;

/// <summary>Answers mail that has just settled on an account whose user's automatic reply is running.</summary>
/// <param name="assignments">Answers whose account it is, and whether it is shared.</param>
/// <param name="store">Holds the user's reply and the senders it already answered.</param>
/// <param name="summaries">Reads what was established about an arrival's sender when it was stored.</param>
/// <param name="contentStore">Holds the raw MIME whose header block decides whether the arrival may be answered.</param>
/// <param name="envelopes">Reads that header block.</param>
/// <param name="senderIdentities">Names the address the account sends from, which is never answered.</param>
/// <param name="composer">Composes the reply.</param>
/// <param name="outbox">Writes the reply down for the account's own delivery to send.</param>
/// <param name="timeProvider">Says whether the reply's period has begun or ended.</param>
/// <remarks>
/// <para>
/// It is called by the settlement, once an arrival's spam verdict and its rules have placed it, and only for what
/// settled on this replica: settling is one conditional statement, so of two replicas reaching one message only one
/// is handed it, and that replica is the only one that can answer it. A sender is then answered once per revision of
/// the reply, through a claimed row, so two messages from one sender settling on two replicas at once are answered
/// once between them.
/// </para>
/// <para>
/// It reads no custody phase, because nothing about it differs by phase: every phase stores new mail by
/// synchronization and settles it through the same pass, and every phase sends through the account's outbox. A message
/// a held account files locally or a restore appends back is stored already settled, so it never reaches this.
/// </para>
/// <para>
/// Only mail the mailbox received since the period started is answered, by the date its last receiving hop recorded:
/// a backfill or a resynchronization settles old mail during the period too, and answering it would write to every
/// correspondent of years of history.
/// </para>
/// <para>
/// The claim is taken before the reply is written down and the two are not one transaction, so a failure between them
/// leaves that sender unanswered for the rest of the revision rather than answered twice. A reply the deployment refuses
/// to send — sending switched off, a recipient its policy refuses, a period ceiling, a screen — is not retried either.
/// </para>
/// </remarks>
public sealed class AutomaticReplyResponder(
    IMailAccountAssignments assignments,
    IAutomaticReplyStore store,
    IStoredEmailSummaryReader summaries,
    IEmailContentStore contentStore,
    IArrivedMailEnvelopeReader envelopes,
    IOutgoingSenderIdentityReader senderIdentities,
    IAuthoredEmailComposer composer,
    MailOutbox outbox,
    TimeProvider timeProvider)
{
    /// <summary>Answers the arrivals that may be answered, while the account's user's reply runs.</summary>
    /// <param name="account">The account the mail settled on.</param>
    /// <param name="arrivals">The arrivals that settled on this replica and that a person would be told about: not junk, not in junk or trash, not deleted.</param>
    /// <param name="cancellationToken">Cancels the answering between arrivals.</param>
    /// <returns>How many replies were written down.</returns>
    public async Task<int> AnswerAsync(
        MailAccountId account,
        IReadOnlyList<StoredEmailId> arrivals,
        CancellationToken cancellationToken)
    {
        if (arrivals.Count == 0)
        {
            return 0;
        }

        // A shared mailbox is never answered, however the reply was saved: it is assigned to several users, and nobody
        // is away on its behalf.
        if (await assignments.ReadUsersAssignedToAsync(account, cancellationToken) is not [var user]
            || await store.ReadAsync(user, cancellationToken) is not { } held
            || !held.Reply.IsAnswering(account, timeProvider.GetUtcNow())
            || senderIdentities.FindSenderIdentity(account) is not { } sender)
        {
            return 0;
        }

        var answered = 0;

        foreach (var arrival in arrivals)
        {
            if (await this.AnswerOneAsync(account, user, held, sender.Address, arrival, cancellationToken))
            {
                answered++;
            }
        }

        return answered;
    }

    private async Task<bool> AnswerOneAsync(
        MailAccountId account,
        UserId user,
        StoredAutomaticReply held,
        EmailAddress ownAddress,
        StoredEmailId arrival,
        CancellationToken cancellationToken)
    {
        if (await summaries.FindAsync(arrival, cancellationToken) is not { ContentAvailability: StoredEmailContentAvailability.Available } summary
            || !ReachedTheMailboxDuring(held.Reply, summary.ReceivedAt)
            || await this.ReadEnvelopeAsync(arrival, cancellationToken) is not { } envelope
            || !envelope.TryReadAnswerable(ownAddress, out var target))
        {
            return false;
        }

        var insideOrganization = summary.SenderVerification.DeploymentTrustGrantedBy is SenderTrustSource.OrganizationMember
            && envelope.AnswersTheAuthor(target);

        var composition = composer.Compose(
            account,
            user,
            OutgoingEmailRequester.AutomaticReply(arrival),
            new AuthoredEmail
            {
                Recipients = [new AuthoredEmailRecipient(OutgoingRecipientRole.To, target.Address, target.DisplayName)],
                Subject = ResponseSubject.ForAutomaticReply(envelope.Subject),
                PlainTextBody = held.Reply.TextFor(insideOrganization),
                Threading = OutgoingThreadPlacement.Answering(envelope.ThreadReferences),
                IsAutomaticReply = true,
            },
            MailDeliveryCapabilities.BeforeAnyServerHasSpoken);

        if (composition.Email is not { } composed
            || !await store.TryClaimAsync(user, held.Revision, target, cancellationToken))
        {
            return false;
        }

        return await this.WriteDownAsync(composed, cancellationToken);
    }

    /// <summary>Answers whether the message reached the mailbox after the reply started, rather than being history a synchronization only now stored.</summary>
    /// <remarks>
    /// Settling now says nothing about when a message arrived: a mailbox's first runs, and a resynchronization after its
    /// folders were renumbered, store years of read mail that settles during the period like anything new. The last
    /// receiving hop is the mailbox's own server, so its date is what tells the two apart, and mail carrying none is
    /// not answered because nothing says it is new.
    /// </remarks>
    private static bool ReachedTheMailboxDuring(AutomaticReply reply, DateTimeOffset? receivedAt) =>
        receivedAt is { } received && received >= reply.StartsAt;

    private async Task<ArrivedMailEnvelope?> ReadEnvelopeAsync(StoredEmailId arrival, CancellationToken cancellationToken)
    {
        var content = await contentStore.FindStoredContentAsync(arrival, cancellationToken);

        return content is null || content.FindIntegrityDefect() is not null ? null : envelopes.Read(content.RawMime);
    }

    private async Task<bool> WriteDownAsync(ComposedOutgoingEmail composed, CancellationToken cancellationToken)
    {
        try
        {
            await outbox.EnqueueAsync(composed.Request, composed.RawMime, cancellationToken);

            return true;
        }
        catch (Exception exception) when (exception is OutgoingMailRefusedException or SensitiveContentScannerUnavailableException)
        {
            return false;
        }
    }
}
