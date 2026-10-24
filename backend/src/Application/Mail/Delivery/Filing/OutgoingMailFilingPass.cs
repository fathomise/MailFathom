// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Accounts.Custody;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Filing;

namespace MailFathom.Application.Mail.Delivery.Filing;

/// <summary>Keeps the mailbox's copies of one account's outgoing mail in step with what the outbox is doing.</summary>
/// <remarks>
/// <para>
/// Two moments, and they are the two an outbox pass already reaches. After a send has settled it files the sent copy
/// where the account asked for one, and once synchronization has met a second occurrence of a filed sent copy it takes
/// the appended one back out where the account asked for that.
/// </para>
/// <para>
/// Nothing here files a copy of a send that has not left. What is waiting is the account's outbox, which MailFathom
/// keeps beside the account's folders rather than in one of them, as
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0040-each-account-outbox-as-a-read-only-list-beside-its-folders.md">ADR 0040</see>
/// decides.
/// </para>
/// <para>
/// Whether an account keeps any copy of a send on a server at all is its custodian's to answer, so each entry asks
/// <see cref="MailboxCustodianResolver" /> and the custodian of an account whose copies are kept on a server comes back
/// to the half of this type that keeps them.
/// </para>
/// </remarks>
public sealed class OutgoingMailFilingPass
{
    /// <summary>
    /// How long after an append a second occurrence of the message is still read as the provider having filed its own.
    /// </summary>
    /// <remarks>
    /// It bounds a race between one append and the synchronization run that meets what the provider filed, which is
    /// minutes on any account being synchronized at all. Past it the copy has been in the user's folder long enough to
    /// have been read, replied from, moved, or flagged, and a duplicate discovered then is more likely to be the user's
    /// own act than the provider's — so both copies stand and the one the user does not want is the one they delete. It
    /// is a constant rather than a setting because it measures how long this system waits to recognize its own copy,
    /// which is not a quantity an operator has grounds to tune.
    /// </remarks>
    private static readonly TimeSpan DuplicateSentCopyWindow = TimeSpan.FromMinutes(15);

    private readonly IOutgoingEmailStore outgoingEmails;
    private readonly OutgoingMailFiler filer;
    private readonly IOutgoingMailFilingPolicyReader filingPolicies;
    private readonly IOutgoingMailFilingStore filings;
    private readonly MailboxCustodianResolver custodians;
    private readonly TimeProvider timeProvider;
    private readonly int maxWithdrawalsPerPass;

    /// <summary>Initializes the pass from the outbox it reads and the filer it acts through.</summary>
    /// <param name="outgoingEmails">Holds the records whose copies this pass keeps in step.</param>
    /// <param name="filer">Appends and withdraws one copy.</param>
    /// <param name="filingPolicies">Answers whether this account files a copy of what it sends.</param>
    /// <param name="filings">Answers which filed sent copies the folder holds a second occurrence of.</param>
    /// <param name="custodians">Chooses whether an account keeps copies of its sends on a server at all.</param>
    /// <param name="timeProvider">Decides how recently a sent copy was appended.</param>
    /// <param name="settings">Bounds how many sent copies one pass withdraws.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    /// <remarks>
    /// The withdrawals are bounded by the same number as the deliveries, because both are one conversation with a server
    /// per send and what one pass leaves the next one takes. A second bound would be a second number to keep in step for
    /// no difference an operator could observe.
    /// </remarks>
    public OutgoingMailFilingPass(
        IOutgoingEmailStore outgoingEmails,
        OutgoingMailFiler filer,
        IOutgoingMailFilingPolicyReader filingPolicies,
        IOutgoingMailFilingStore filings,
        MailboxCustodianResolver custodians,
        TimeProvider timeProvider,
        MailOutboxSettings settings)
    {
        ArgumentNullException.ThrowIfNull(outgoingEmails);
        ArgumentNullException.ThrowIfNull(filer);
        ArgumentNullException.ThrowIfNull(filingPolicies);
        ArgumentNullException.ThrowIfNull(filings);
        ArgumentNullException.ThrowIfNull(custodians);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(settings);

        this.outgoingEmails = outgoingEmails;
        this.filer = filer;
        this.filingPolicies = filingPolicies;
        this.filings = filings;
        this.custodians = custodians;
        this.timeProvider = timeProvider;
        this.maxWithdrawalsPerPass = settings.MaxDeliveriesPerPass;
    }

    /// <summary>Takes back the sent copies whose provider has since filed one of its own beside them.</summary>
    /// <param name="account">The account whose sent folder is brought back to one copy of each message.</param>
    /// <param name="cancellationToken">Cancels the read and the withdrawals.</param>
    /// <returns>What each withdrawal did, which is empty on an account that has asked for none.</returns>
    /// <remarks>
    /// <para>
    /// A provider that files the sent copy itself does it asynchronously, so nothing before the send can tell whether it
    /// will. What can be told, once synchronization has met a second occurrence of the message in the same folder, is
    /// that the folder now holds two — and the one this system appended is the one it may take back out.
    /// </para>
    /// <para>
    /// It runs on the outbox pass rather than on the synchronization run that discovers the duplicate, because taking a
    /// message out of a folder is a write and no read path may obtain the session that makes one. The cost is one query
    /// per pass on an account whose sent folder holds no duplicate, which is every account whose provider files
    /// nothing. A held account withdraws nothing: its source is drained, and a copy appended there while it was still
    /// mirrored is the drain's to remove.
    /// </para>
    /// <para>
    /// One pass withdraws no more copies than it delivers sends: each is one conversation with a server, and what one pass leaves the next one takes. A record an erasure
    /// removed between the read and the withdrawal is passed over rather than reported, because the copy it named
    /// belongs to a message this deployment no longer holds.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<OutgoingMailFilingResult>> WithdrawDuplicatedSentCopiesAsync(
        MailAccountId account,
        CancellationToken cancellationToken)
    {
        if (!this.filingPolicies.WithdrawsDuplicateSentCopy(account))
        {
            return [];
        }

        var custodian = await this.custodians.ResolveAsync(account, cancellationToken);

        return await custodian.WithdrawDuplicatedSentCopiesAsync(account, cancellationToken);
    }

    /// <summary>Withdraws, over IMAP, the appended sent copies the provider filed a copy of its own beside.</summary>
    /// <param name="account">The account whose sent folder is brought back to one copy of each message.</param>
    /// <param name="cancellationToken">Cancels the read and the withdrawals.</param>
    /// <returns>What each withdrawal did.</returns>
    internal async Task<IReadOnlyList<OutgoingMailFilingResult>> WithdrawDuplicatedSentCopiesOnSourceAsync(
        MailAccountId account,
        CancellationToken cancellationToken)
    {
        var duplicated = await this.filings.ReadDuplicatedSentCopiesAsync(
            account,
            this.timeProvider.GetUtcNow() - DuplicateSentCopyWindow,
            this.maxWithdrawalsPerPass,
            cancellationToken);

        var results = new List<OutgoingMailFilingResult>(duplicated.Count);
        foreach (var outgoingEmailId in duplicated)
        {
            if (await this.outgoingEmails.FindAsync(outgoingEmailId, cancellationToken) is not { } record)
            {
                continue;
            }

            results.Add(await this.filer.WithdrawAsync(record, OutgoingMailFiling.Sent, cancellationToken));
        }

        return results;
    }

    /// <summary>Files the sent copy of one settled send, where its account asked for one.</summary>
    /// <param name="outgoingEmailId">The send whose attempt has just ended.</param>
    /// <param name="cancellationToken">Cancels the reads and the appends.</param>
    /// <returns>What each attempt did, which is empty for a send that is still queued.</returns>
    /// <remarks>
    /// <para>
    /// The record is read again rather than taken from the attempt, because the attempt handed back what it decided and
    /// this needs what was committed — including the filing rows an earlier pass wrote, which no delivery result
    /// carries.
    /// </para>
    /// <para>
    /// The sent copy is appended only after a delivery the server acknowledged, which is the one point at which saying
    /// <em>this was sent</em> is true. It is keyed on the send having reached a terminal stage rather than on how it got
    /// there, so every path that settles one calls this, and a send that ended any other way files nothing.
    /// </para>
    /// <para>
    /// A held account is settled with nothing issued at all. Its sent copy was filed into the local sent folder in the
    /// commit that recorded the delivery, and an append to a drained source would be drained straight back.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<OutgoingMailFilingResult>> SettleFiledCopiesAsync(
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        if (await this.outgoingEmails.FindAsync(outgoingEmailId, cancellationToken) is not { IsTerminal: true } record)
        {
            return [];
        }

        var custodian = await this.custodians.ResolveAsync(record.AccountId, cancellationToken);

        return await custodian.SettleFiledCopiesAsync(record, cancellationToken);
    }

    /// <summary>Appends a settled send's sent copy on the account's server.</summary>
    /// <param name="record">The send, read after its attempt ended.</param>
    /// <param name="cancellationToken">Cancels the appends.</param>
    /// <returns>What each attempt did.</returns>
    internal async Task<IReadOnlyList<OutgoingMailFilingResult>> SettleFiledCopiesOnSourceAsync(
        OutgoingEmailRecord record,
        CancellationToken cancellationToken)
    {
        if (record.Stage != OutgoingEmailStage.Sent)
        {
            return [];
        }

        if (!this.filingPolicies.FilesSentCopy(record.AccountId))
        {
            return
            [
                new OutgoingMailFilingResult(
                    record.Id,
                    OutgoingMailFiling.Sent,
                    OutgoingMailFilingOutcome.NotRequested,
                    Failure: null),
            ];
        }

        return [await this.filer.FileAsync(record, OutgoingMailFiling.Sent, cancellationToken)];
    }
}
