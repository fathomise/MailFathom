// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Mail.Delivery.Filing;

/// <summary>Puts a copy of an outgoing message into the folder the message's own state calls for.</summary>
/// <remarks>
/// <para>
/// One mechanism for every place a message MailFathom authored belongs: a draft in the drafts folder and a delivered
/// message in the sent folder. What differs between them is the role of the destination and the flags the copy
/// carries, and both travel on <see cref="OutgoingMailFiling" /> — so a third place is a member there rather than a
/// second filer here.
/// </para>
/// <para>
/// The destination is found by role and never by name, because every one of these folders is spelled differently in
/// every locale and by every provider. A role no folder of the account plays is a destination that is not available,
/// which is the same answer a mapped folder the server does not advertise gives: the account's folder mapping is what
/// changes it, and the message is untouched either way.
/// </para>
/// <para>
/// The append itself is <see cref="MailboxCopyAppender" />'s, and with it the order of the two writes that is the whole
/// of the safety here: the row is durable at <see cref="OutgoingMailFilingStage.Issued" /> before the command goes out,
/// and a row found there is reported rather than appended again. What stays here is which row moves and what a copy
/// that could not be filed means for the send it belongs to.
/// </para>
/// <para>
/// Nothing here can fail a delivery. A send whose copy could not be filed is a send that happened, so the failure is
/// written beside the delivery stage rather than over it, and no delivery is ever attempted again because of one.
/// </para>
/// </remarks>
public sealed class OutgoingMailFiler
{
    private readonly MailboxCopyAppender appends;
    private readonly IMailboxWriteSessionFactory writeSessions;
    private readonly MailboxDestinationResolver destinations;
    private readonly IOutgoingMailFilingStore filings;
    private readonly IMailTransportSecurityPolicyReader transportSecurityPolicies;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the filer from the append it files through and the record it writes the copy onto.</summary>
    /// <param name="appends">Puts the copy into the folder the filing names, in the order that keeps it one copy.</param>
    /// <param name="writeSessions">Opens the one session able to change a mailbox, which removing a provider's copy needs of its own.</param>
    /// <param name="destinations">Turns the role a filing names into the folder of this account it means.</param>
    /// <param name="filings">Keeps the durable account of every copy that has been filed.</param>
    /// <param name="transportSecurityPolicies">Supplies the connection and authentication policy the commands obey.</param>
    /// <param name="commitPolicy">Commits each movement of the filing row.</param>
    /// <param name="timeProvider">Stamps everything the row records.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public OutgoingMailFiler(
        MailboxCopyAppender appends,
        IMailboxWriteSessionFactory writeSessions,
        MailboxDestinationResolver destinations,
        IOutgoingMailFilingStore filings,
        IMailTransportSecurityPolicyReader transportSecurityPolicies,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(appends);
        ArgumentNullException.ThrowIfNull(writeSessions);
        ArgumentNullException.ThrowIfNull(destinations);
        ArgumentNullException.ThrowIfNull(filings);
        ArgumentNullException.ThrowIfNull(transportSecurityPolicies);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.appends = appends;
        this.writeSessions = writeSessions;
        this.destinations = destinations;
        this.filings = filings;
        this.transportSecurityPolicies = transportSecurityPolicies;
        this.commitPolicy = commitPolicy;
        this.timeProvider = timeProvider;
    }

    /// <summary>Files one copy of a message into the place its state calls for.</summary>
    /// <param name="record">The outgoing record the copy is filed from, read with its existing filings.</param>
    /// <param name="filing">Which place the copy goes into.</param>
    /// <param name="cancellationToken">Cancels the append and the writes around it.</param>
    /// <returns>What the attempt did, which is already durable by the time it is returned.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="record" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="filing" /> is the unspecified struct default.</exception>
    /// <remarks>
    /// It does not raise for a copy that could not be filed. Everything the attempt learned is written onto the record
    /// first, so a caller reads the outcome rather than catching it — which is what lets a filing failure leave the
    /// delivery that preceded it exactly as it was.
    /// </remarks>
    public async Task<OutgoingMailFilingResult> FileAsync(
        OutgoingEmailRecord record,
        OutgoingMailFiling filing,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        RequireSpecified(filing);

        if (SettledOutcomeOf(record, filing) is { } settled)
        {
            return Result(record, filing, settled, failure: null);
        }

        var attempted = await this.AttemptAppendAsync(record, filing, cancellationToken);

        return record.Stage == OutgoingEmailStage.Sent
            ? attempted with { WaitedAfterDelivery = this.timeProvider.GetUtcNow() - record.StageChangedAt }
            : attempted;
    }

    /// <summary>Appends the copy and turns every failure the append can end in into a recorded outcome.</summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A filing failure must never reach the delivery that preceded it, because a send whose copy could not be filed is a send that happened; the failure is classified into a recorded code and returned as an outcome rather than raised.")]
    private async Task<OutgoingMailFilingResult> AttemptAppendAsync(
        OutgoingEmailRecord record,
        OutgoingMailFiling filing,
        CancellationToken cancellationToken)
    {
        try
        {
            return await this.AppendAsync(record, filing, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller stopping is not something that happened to the copy. Everything past the issued write is
            // caught where it happens, so a cancellation reaching here left the mail server untouched and the next
            // pass files the copy as though this attempt had never started — which is what a caller that already
            // sorts a shutdown from a failure is waiting to be told.
            throw;
        }
        catch (Exception failure)
        {
            // Nothing reached the mail server: everything past the issued write is caught where it happens, and the
            // row this would have been written against was never opened.
            return await this.RecordFailureAsync(
                record,
                filing,
                MailboxCopyAppender.FailureCodeOf(failure),
                OutgoingMailFilingOutcome.Failed);
        }
    }

    /// <summary>Removes the copy a provider filed of a send beside the copy this deployment appended.</summary>
    /// <param name="account">The account the send belongs to.</param>
    /// <param name="filing">The filing whose provider copy synchronization recorded.</param>
    /// <param name="cancellationToken">Cancels the removal.</param>
    /// <returns>What the attempt did.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="filing" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// A removal that ends in a refusal the server will repeat — the folder renumbered, no <c>UIDPLUS</c>, the UID
    /// naming another message, the role resolving to another folder — settles the row with the provider's copy kept, so
    /// nothing asks again. Every other failure leaves the row as it was, for the next pass to try within the window the
    /// caller bounds it by.
    /// </para>
    /// <para>
    /// A copy the folder no longer holds is removed as far as anybody can tell, because the user deleting it themselves
    /// is the ordinary case and what the removal asked for is already true.
    /// </para>
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A provider copy that could not be removed is one message left beside the person's own sent copy; raising would end the pass that was settling the other sends beside it.")]
    public async Task<OutgoingMailFilingResult> RemoveProviderCopyAsync(
        MailAccountId account,
        OutgoingMailFilingRecord filing,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filing);

        if (filing is not
            {
                AwaitsProviderCopyRemoval: true,
                ProviderCopy: { UidValidity: { } uidValidity, Uid: { } uid },
                InternetMessageId: { } internetMessageId,
            })
        {
            return new(filing.OutgoingEmailId, filing.Filing, OutgoingMailFilingOutcome.NotRequested, Failure: null);
        }

        try
        {
            var removed = await this.RemoveFromServerAsync(
                account,
                filing,
                uidValidity,
                uid,
                internetMessageId,
                cancellationToken);

            return await this.SettleProviderCopyAsync(
                filing,
                removed ? OutgoingMailFilingOutcome.ProviderCopyRemoved : OutgoingMailFilingOutcome.ProviderCopyKept);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception refusal) when (refusal is MailboxFolderRecreatedException or MailboxMutationUnsupportedException)
        {
            return await this.SettleProviderCopyAsync(filing, OutgoingMailFilingOutcome.ProviderCopyKept);
        }
        catch (Exception failure)
        {
            return new(
                filing.OutgoingEmailId,
                filing.Filing,
                OutgoingMailFilingOutcome.Failed,
                MailboxCopyAppender.FailureCodeOf(failure));
        }
    }

    /// <summary>Settles a provider copy nothing will try to remove again, which is not a removal whatever happened to it.</summary>
    /// <param name="filing">The filing whose provider copy is settled.</param>
    /// <returns>What became of the copy, once the row is settled.</returns>
    public Task<OutgoingMailFilingResult> SettleProviderCopyKeptAsync(OutgoingMailFilingRecord filing) =>
        this.SettleProviderCopyAsync(filing, OutgoingMailFilingOutcome.ProviderCopyKept);

    private async Task<OutgoingMailFilingResult> SettleProviderCopyAsync(
        OutgoingMailFilingRecord filing,
        OutgoingMailFilingOutcome outcome)
    {
        await this.commitPolicy.CommitAsync(
            (session, token) => this.filings.RecordProviderCopySettledAsync(
                session,
                filing.OutgoingEmailId,
                filing.Filing,
                this.timeProvider.GetUtcNow(),
                token),
            CancellationToken.None);

        return new(filing.OutgoingEmailId, filing.Filing, outcome, Failure: null);
    }

    private static void RequireSpecified(OutgoingMailFiling filing)
    {
        if (!filing.IsSpecified)
        {
            throw new ArgumentException("The unspecified default of the struct names no filing.", nameof(filing));
        }
    }

    /// <summary>Answers from the record alone where the record has already settled what asking again means.</summary>
    private static OutgoingMailFilingOutcome? SettledOutcomeOf(OutgoingEmailRecord record, OutgoingMailFiling filing) =>
        record.FindFiling(filing) switch
        {
            null => null,
            { HasUnknownOutcome: true } => OutgoingMailFilingOutcome.OutcomeUnknown,
            _ => OutgoingMailFilingOutcome.AlreadyFiled,
        };

    /// <summary>Reads what the append reported as what it means for the send the copy belongs to.</summary>
    /// <remarks>
    /// <see cref="MailboxCopyAppendOutcome.Appended" /> never reaches here, because a result carrying no failure is the
    /// copy that was filed. What is left is a message that could not be appended, which fails the filing and no more.
    /// </remarks>
    private static OutgoingMailFilingOutcome FilingOutcomeOf(MailboxCopyAppendOutcome outcome) => outcome switch
    {
        MailboxCopyAppendOutcome.DestinationUnavailable => OutgoingMailFilingOutcome.DestinationUnavailable,
        MailboxCopyAppendOutcome.OutcomeUnknown => OutgoingMailFilingOutcome.OutcomeUnknown,
        _ => OutgoingMailFilingOutcome.Failed,
    };

    private static OutgoingMailFilingResult Result(
        OutgoingEmailRecord record,
        OutgoingMailFiling filing,
        OutgoingMailFilingOutcome outcome,
        MailFathomErrorCode? failure) => new(record.Id, filing, outcome, failure);

    /// <summary>Files the copy, moving the filing row through the stages the append passes.</summary>
    private async Task<OutgoingMailFilingResult> AppendAsync(
        OutgoingEmailRecord record,
        OutgoingMailFiling filing,
        CancellationToken cancellationToken)
    {
        var appended = await this.appends.AppendAsync(
            record.AccountId,
            filing,
            MailboxCopySource.SentCopy(record),
            (binding, token) => this.commitPolicy.CommitAsync(
                (persistenceSession, commitToken) => this.filings.RecordAppendIssuedAsync(
                    persistenceSession,
                    record.Id,
                    filing,
                    binding,
                    this.timeProvider.GetUtcNow(),
                    commitToken),
                token),
            copy => this.commitPolicy.CommitAsync(
                (persistenceSession, commitToken) => this.filings.RecordAppendConfirmedAsync(
                    persistenceSession,
                    record.Id,
                    filing,
                    copy,
                    commitToken),
                CancellationToken.None),
            cancellationToken);

        if (appended.Failure is not { } failure)
        {
            return Result(record, filing, OutgoingMailFilingOutcome.Filed, failure: null);
        }

        return await this.RecordFailureAsync(record, filing, failure, FilingOutcomeOf(appended.Outcome));
    }

    /// <summary>Issues the removal against the folder the provider's copy is in, as that folder currently resolves.</summary>
    /// <returns><see langword="true" /> when the folder no longer holds the copy; <see langword="false" /> when nothing was removed.</returns>
    /// <remarks>
    /// The destination is resolved again rather than opened from the recorded path, because the path is where the folder
    /// was when the copy was appended and an alias that has since been repointed names another folder: a role resolving
    /// elsewhere removes nothing. The recorded UIDVALIDITY is what the session then refuses on, so a folder recreated in
    /// between has nothing removed from it either.
    /// </remarks>
    private async Task<bool> RemoveFromServerAsync(
        MailAccountId account,
        OutgoingMailFilingRecord filing,
        ImapUidValidity uidValidity,
        ImapUid uid,
        string internetMessageId,
        CancellationToken cancellationToken)
    {
        var reference = MailFolderReference.ToRole(filing.Filing.Role);
        var resolution = (await this.destinations.ResolveAsync(account, [reference], cancellationToken)).Find(reference);

        if (resolution.Destination is not { } destination
            || !destination.Path.NamesSameFolderAs(filing.FolderPath))
        {
            return false;
        }

        await using var session = await this.writeSessions.OpenForWritingAsync(
            account,
            destination.Binding,
            this.transportSecurityPolicies.GetPolicy(account),
            cancellationToken);

        return await session.RemoveProviderSentCopyAsync(uidValidity, uid, internetMessageId, cancellationToken);
    }

    /// <summary>Writes the reason a copy is not where it should be onto the record, without touching its delivery.</summary>
    /// <remarks>
    /// The write runs outside the caller's cancellation on purpose, and joins no transaction of the caller's. What it
    /// records is why the user cannot see a message in their own mail client, and a host that stopped while recording
    /// it would leave a send that says it was filed and was not.
    /// </remarks>
    private async Task<OutgoingMailFilingResult> RecordFailureAsync(
        OutgoingEmailRecord record,
        OutgoingMailFiling filing,
        MailFathomErrorCode failure,
        OutgoingMailFilingOutcome outcome)
    {
        await this.filings.RecordFilingFailureAsync(record.Id, failure, CancellationToken.None);

        return Result(record, filing, outcome, failure);
    }
}
