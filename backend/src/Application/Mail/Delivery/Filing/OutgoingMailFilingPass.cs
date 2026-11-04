// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Accounts.Custody;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Filing;

namespace MailFathom.Application.Mail.Delivery.Filing;

/// <summary>Keeps the mailbox's copies of one account's outgoing mail in step with what the outbox is doing.</summary>
/// <remarks>
/// <para>
/// Two moments, and they are the two an outbox pass already reaches. After a send has settled it files the sent copy
/// where the account asked for one, and once synchronization has recorded a copy the provider filed of the same send
/// beside it, it removes the provider's copy, so the copy this deployment stored and appended is the one the folder keeps.
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
    /// <summary>How long after an append a provider's copy recorded for removal is still removed rather than kept.</summary>
    /// <remarks>
    /// The copy was met within <see cref="OutgoingMailFilingRecord.ProviderCopyWindow" /> of the append, and a removal
    /// the server could not serve is asked again on every pass for as long again. Past that the copy has stood beside the
    /// person's own long enough for another client to have acted on it, so it is kept.
    /// </remarks>
    private static readonly TimeSpan ProviderCopyRemovalDeadline = OutgoingMailFilingRecord.ProviderCopyWindow * 2;

    private readonly IOutgoingEmailStore outgoingEmails;
    private readonly OutgoingMailFiler filer;
    private readonly IOutgoingMailFilingPolicyReader filingPolicies;
    private readonly IOutgoingMailFilingStore filings;
    private readonly MailboxCustodianResolver custodians;
    private readonly IEmailContentStore contents;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly TimeProvider timeProvider;
    private readonly int maxRemovalsPerPass;

    /// <summary>Initializes the pass from the outbox it reads and the filer it acts through.</summary>
    /// <param name="outgoingEmails">Holds the records whose copies this pass keeps in step.</param>
    /// <param name="filer">Appends one copy, and removes the copy a provider filed beside it.</param>
    /// <param name="filingPolicies">Answers whether this account files a copy of what it sends.</param>
    /// <param name="filings">Answers which provider copies synchronization recorded for removal.</param>
    /// <param name="custodians">Chooses whether an account keeps copies of its sends on a server at all.</param>
    /// <param name="contents">Releases a delivered send's stored message once its sent copy no longer needs it.</param>
    /// <param name="commitPolicy">Commits that release.</param>
    /// <param name="timeProvider">Decides how recently a sent copy was appended.</param>
    /// <param name="settings">Bounds how many provider copies one pass removes.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    /// <remarks>
    /// The removals are bounded by the same number as the deliveries, because both are one conversation with a server
    /// per send and what one pass leaves the next one takes. A second bound would be a second number to keep in step for
    /// no difference an operator could observe.
    /// </remarks>
    public OutgoingMailFilingPass(
        IOutgoingEmailStore outgoingEmails,
        OutgoingMailFiler filer,
        IOutgoingMailFilingPolicyReader filingPolicies,
        IOutgoingMailFilingStore filings,
        MailboxCustodianResolver custodians,
        IEmailContentStore contents,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        TimeProvider timeProvider,
        MailOutboxSettings settings)
    {
        ArgumentNullException.ThrowIfNull(outgoingEmails);
        ArgumentNullException.ThrowIfNull(filer);
        ArgumentNullException.ThrowIfNull(filingPolicies);
        ArgumentNullException.ThrowIfNull(filings);
        ArgumentNullException.ThrowIfNull(custodians);
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(settings);

        this.outgoingEmails = outgoingEmails;
        this.filer = filer;
        this.filingPolicies = filingPolicies;
        this.filings = filings;
        this.custodians = custodians;
        this.contents = contents;
        this.commitPolicy = commitPolicy;
        this.timeProvider = timeProvider;
        this.maxRemovalsPerPass = settings.MaxDeliveriesPerPass;
    }

    /// <summary>Removes the copies providers filed of the account's sends beside the copies this deployment stored and appended.</summary>
    /// <param name="account">The account whose sent folder is brought back to one copy of each message.</param>
    /// <param name="cancellationToken">Cancels the read and the removals.</param>
    /// <returns>What each removal did, which is empty on an account with nothing recorded.</returns>
    /// <remarks>
    /// <para>
    /// A provider that files the sent copy itself does it asynchronously, so nothing before the send can tell whether it
    /// will. Synchronization recognizes the provider's copy as it meets it, stores no row for it, and records it for
    /// removal only where removing a message this deployment did not compose is safe; everything else about it is
    /// decided there.
    /// </para>
    /// <para>
    /// It runs on the outbox pass rather than on the synchronization run that met the copy, because removing a message
    /// is a write and no read path may obtain the session that makes one. The cost is one indexed query per pass on an
    /// account with nothing recorded, which is every account whose provider files nothing. A held account removes
    /// nothing: its source is drained.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<OutgoingMailFilingResult>> RemoveProviderSentCopiesAsync(
        MailAccountId account,
        CancellationToken cancellationToken)
    {
        var custodian = await this.custodians.ResolveAsync(account, cancellationToken);

        return await custodian.RemoveProviderSentCopiesAsync(account, cancellationToken);
    }

    /// <summary>Removes, over IMAP, the provider copies synchronization recorded beside the account's own sent copies.</summary>
    /// <param name="account">The account whose sent folder is brought back to one copy of each message.</param>
    /// <param name="cancellationToken">Cancels the read and the removals.</param>
    /// <returns>What each removal did.</returns>
    /// <remarks>
    /// One pass removes no more copies than it delivers sends, and a copy whose removal the server could not serve before
    /// <see cref="ProviderCopyRemovalDeadline" /> is kept without asking it again.
    /// </remarks>
    internal async Task<IReadOnlyList<OutgoingMailFilingResult>> RemoveProviderSentCopiesOnSourceAsync(
        MailAccountId account,
        CancellationToken cancellationToken)
    {
        var awaiting = await this.filings.ReadProviderCopiesAwaitingRemovalAsync(
            account,
            this.maxRemovalsPerPass,
            cancellationToken);

        var results = new List<OutgoingMailFilingResult>(awaiting.Count);
        foreach (var filing in awaiting)
        {
            results.Add(this.timeProvider.GetUtcNow() - filing.AppendedAt > ProviderCopyRemovalDeadline
                ? await this.filer.SettleProviderCopyKeptAsync(filing)
                : await this.filer.RemoveProviderCopyAsync(account, filing, cancellationToken));
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
    /// <para>
    /// Once the custodian has answered, a delivered send's stored message has no reader left: every recipient was
    /// answered, an append is never issued twice — whether the server confirmed it, never answered it, or refused it —
    /// and a held account's copy was prepared before its delivery committed. So the payload is released here, in every
    /// phase alike, and the record and its recipients stay as what was sent. A custodian that raised has not settled the
    /// copy, so the payload stays for the sweep to release once nothing can still be filing it.
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

        var results = await custodian.SettleFiledCopiesAsync(record, cancellationToken);

        if (record.Stage == OutgoingEmailStage.Sent)
        {
            await this.ReleaseDeliveredContentAsync(record.Id, cancellationToken);
        }

        return results;
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

    /// <summary>Releases a settled delivery's stored message, and never lets that change what the filing reports.</summary>
    /// <remarks>
    /// The filing already happened and is already durable, so a release that could not be committed must not turn it
    /// into a reported failure: the payload simply stays until <see cref="DeliveredOutgoingContentSweep" /> reaches it.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The filing outcomes this pass returns are already durable; a release that failed or was stopped leaves a payload the sweep releases later, and raising would report a settled copy as one that failed.")]
    [SuppressMessage("Roslynator", "RCS1075:Avoid empty catch clause that catches System.Exception", Justification = "There is no second action to take: the payload left standing is the sweep's to release.")]
    private async Task ReleaseDeliveredContentAsync(OutgoingEmailId outgoingEmailId, CancellationToken cancellationToken)
    {
        try
        {
            await this.commitPolicy.CommitAsync(
                (session, token) => this.contents.ReleaseDeliveredOutgoingContentAsync(session, [outgoingEmailId], token),
                cancellationToken);
        }
        catch (Exception)
        {
            // Deliberately silent: the copy is settled whatever happened here, and the payload the release left standing
            // is one the sweep reaches on its next run.
        }
    }
}
