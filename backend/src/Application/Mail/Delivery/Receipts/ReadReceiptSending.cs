// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Localization;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Domain.Access;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;

namespace MailFathom.Application.Mail.Delivery.Receipts;

/// <summary>Sends the read receipt a received message asked for, because the reader asked to — and only then.</summary>
/// <remarks>
/// <para>
/// This is the only route to a read receipt. Nothing sends one when a message is opened, marked seen, matched by a rule,
/// or read by an MCP caller: opening a message is not consent to tell its sender so.
/// </para>
/// <para>
/// A receipt is sent at most once per message, for the account and so for every user assigned to it. The claim is taken
/// before the receipt is written down to be sent, so a failure between the two means the message is never answered rather
/// than answered twice; a refusal the outbox gives back is the one case the claim is released, because nothing left.
/// </para>
/// <para>
/// It goes from the receiving account to the requested address through the account's own outbox, like any message the
/// account sends, under the deployment's sending rules and the caller's own ceiling, and is recorded as an authored send.
/// </para>
/// </remarks>
/// <param name="summaries">Reads the message being answered.</param>
/// <param name="scopeResolver">Decides whether the caller reads that message.</param>
/// <param name="senderIdentities">Names the address the account sends as.</param>
/// <param name="store">Claims the one receipt a message may be answered with.</param>
/// <param name="composer">Composes the receipt.</param>
/// <param name="governor">Admits and records the send as the caller's own.</param>
/// <param name="outbox">Writes the receipt down to be sent.</param>
/// <param name="languages">Reads the language the reader chose, which the receipt's words are written in.</param>
/// <param name="authorization">Establishes who asked and that they may send mail.</param>
/// <param name="timeProvider">Supplies the time the receipt is recorded as sent at.</param>
public sealed class ReadReceiptSending(
    IStoredEmailSummaryReader summaries,
    MailboxScopeResolver scopeResolver,
    IOutgoingSenderIdentityReader senderIdentities,
    IReceiptReportStore store,
    IAuthoredEmailComposer composer,
    AuthoredSendGovernor governor,
    MailOutbox outbox,
    IUserLanguages languages,
    AccessAuthorization authorization,
    TimeProvider timeProvider)
{
    /// <summary>Sends the read receipt one received message asked for.</summary>
    /// <param name="answered">The message the reader is answering.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>How the request ended, with the time the receipt was sent where one was.</returns>
    /// <exception cref="OutgoingMailRefusedException">Thrown when the deployment's sending rules refuse the receipt.</exception>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when what screens outgoing mail could not be reached.</exception>
    public async Task<ReadReceiptSendingResult> SendAsync(StoredEmailId answered, CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.MailSend);

        var user = authorization.RequireUser();
        var summary = await summaries.FindAsync(answered, cancellationToken);

        if (summary is null || !await scopeResolver.IsReadableAsync(MailboxReaderKind.Person, summary, cancellationToken))
        {
            return ReadReceiptSendingResult.Refused(ReadReceiptSendingOutcome.NotFound);
        }

        if (Refuse(summary, senderIdentities.FindSenderIdentity(summary.Account)) is { } refusal)
        {
            return refusal;
        }

        var requested = summary.ReadReceiptRequest!;
        var composition = composer.ComposeReadReceipt(
            summary.Account,
            user,
            OutgoingEmailRequester.ReadReceipt(answered),
            new ReadReceiptAnswer(
                requested.Address,
                ResponseSubject.ForReadReceipt(summary.Subject),
                languages.GetText(ApplicationText.ReadReceiptDisplayed, user),
                summary.InternetMessageId,
                OutgoingThreadPlacement.Answering(EmailThreadReferences.Create(summary.InternetMessageId, null, null))),
            MailDeliveryCapabilities.BeforeAnyServerHasSpoken);

        if (composition.Email is not { } composed)
        {
            return ReadReceiptSendingResult.Refused(
                composition.Refusal!.Reason is AuthoredEmailRefusalReason.SenderUnconfigured
                    ? ReadReceiptSendingOutcome.SenderUnconfigured
                    : ReadReceiptSendingOutcome.Unaddressable);
        }

        return await this.SendComposedAsync(answered, composed, cancellationToken);
    }

    private static ReadReceiptSendingResult? Refuse(EmailSummary summary, OutgoingSenderIdentity? sender)
    {
        if (summary.ReadReceiptRequest is not { } requested)
        {
            return ReadReceiptSendingResult.Refused(ReadReceiptSendingOutcome.NotRequested);
        }

        if (requested.SentAt is { } sentAt)
        {
            return new ReadReceiptSendingResult(ReadReceiptSendingOutcome.AlreadySent, sentAt);
        }

        if (summary.Spam is { Verdict: SpamVerdict.Spam })
        {
            return ReadReceiptSendingResult.Refused(ReadReceiptSendingOutcome.Spam);
        }

        if (sender is null)
        {
            return ReadReceiptSendingResult.Refused(ReadReceiptSendingOutcome.SenderUnconfigured);
        }

        return string.Equals(summary.SenderAddress, sender.Address.Address, StringComparison.OrdinalIgnoreCase)
            ? ReadReceiptSendingResult.Refused(ReadReceiptSendingOutcome.OwnMessage)
            : null;
    }

    private async Task<ReadReceiptSendingResult> SendComposedAsync(
        StoredEmailId answered,
        ComposedOutgoingEmail composed,
        CancellationToken cancellationToken)
    {
        var sentAt = timeProvider.GetUtcNow();

        if (await store.ClaimReadReceiptAsync(answered, sentAt, cancellationToken) is { } earlier)
        {
            return new ReadReceiptSendingResult(ReadReceiptSendingOutcome.AlreadySent, earlier);
        }

        try
        {
            var permit = await governor.RequirePermittedAsync(
                [new AuthoredEmailRecipient(
                    OutgoingRecipientRole.To,
                    composed.Request.Recipients[0].Address.Address,
                    Provenance: AuthoredRecipientProvenance.DerivedFromAnsweredEmail)],
                composed.Request,
                cancellationToken);
            var opened = await outbox.EnqueueAsync(composed.Request, composed.RawMime, cancellationToken);

            if (opened.WasRecordedNow)
            {
                await governor.RecordAsync(permit, AuthoredSendAct.ReadReceipt, opened.Record, cancellationToken);
            }
        }
        catch (Exception refusal) when (refusal is OutgoingMailRefusedException or SensitiveContentScannerUnavailableException)
        {
            // The two failures that certainly wrote nothing down, so the reader may ask again once whatever refused it
            // changes. Any other failure leaves the claim standing, since the receipt may already be on its way.
            await store.ReleaseReadReceiptAsync(answered, sentAt, cancellationToken);
            throw;
        }

        return new ReadReceiptSendingResult(ReadReceiptSendingOutcome.Sent, sentAt);
    }
}
