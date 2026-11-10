// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Accounts.Custody;
using MailFathom.Application.Coordination;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Local;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Retention;

/// <summary>Retires the visible mail past the age its account keeps, through the delete every other act records.</summary>
/// <remarks>
/// <para>
/// Reusing that path is the point: custody, the audit trail, every derived copy, and keeping removed mail removed are
/// what any other delete already does, and what happens on the server is the account's own
/// <see cref="AuthoredDeleteServerDisposition" />, read when the delete is recorded. A held account loses the only copy,
/// which is the decision the parent issue records; a restoring account runs no pass.
/// </para>
/// <para>
/// A folder no run schedules is the one departure. The convergence pass could carry a delete there, but only the
/// folder's own backward pass disposes of the row afterwards, and no backward pass runs for it, so the row would stay
/// hidden with its content while its server copy went. It leaves the server untouched instead, as <c>mfctl folder erase</c>
/// does, and the kept position stops the message being mirrored again if the folder is switched back on.
/// </para>
/// <para>
/// A drained held message has no occurrence, so no request can name it and the trail, whose occurrence columns are
/// required, cannot record it. It is erased directly. Ceiling: such a retirement is counted but not audited. Upgrade
/// path: make those columns nullable and name the local folder instead, as ADR 0034 states.
/// </para>
/// <para>
/// Each batch is a transaction of its own under the custody read inside it, and what the nightly bound leaves is
/// reached on the next occasion. A batch that lost a commit stops the pass, and a later occasion repeats it.
/// </para>
/// </remarks>
public sealed class MailMessageRetirement
{
    /// <summary>The most messages one account has read for retirement in one night.</summary>
    public const int MaximumMessagesReadPerNight = 10_000;

    /// <summary>How many messages one transaction retires.</summary>
    public const int MessagesRetiredPerTransaction = 100;

    private const int MaximumInspectedLeases = 512;

    private readonly IMailMessageRetentionReader retentionReader;
    private readonly IPersistenceSessionFactory sessions;
    private readonly IMailAccountCustodyStore custody;
    private readonly IAgedStoredMailReader agedMail;
    private readonly IAuthoredDeleteEmailDispositionReader deleteDispositions;
    private readonly IMailFolderMappingReader mappings;
    private readonly MailboxChangeSubmission submission;
    private readonly ILocalEmailStateStore states;
    private readonly IWorkLeaseStore leases;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the pass.</summary>
    /// <param name="retentionReader">Reads how long the account keeps its mail.</param>
    /// <param name="sessions">Begins the transaction of each batch.</param>
    /// <param name="custody">Reads which copy of the account's mailbox is the truth.</param>
    /// <param name="agedMail">Reads the mail past the age.</param>
    /// <param name="deleteDispositions">Reads what the account's own deletes do.</param>
    /// <param name="mappings">Says which folders a run schedules.</param>
    /// <param name="submission">Records each delete through whichever custodian holds the account.</param>
    /// <param name="states">Erases a message no request can name.</param>
    /// <param name="leases">Says whether every replica can read a record retention asked for.</param>
    /// <param name="timeProvider">Measures a message's age and names the night.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public MailMessageRetirement(
        IMailMessageRetentionReader retentionReader,
        IPersistenceSessionFactory sessions,
        IMailAccountCustodyStore custody,
        IAgedStoredMailReader agedMail,
        IAuthoredDeleteEmailDispositionReader deleteDispositions,
        IMailFolderMappingReader mappings,
        MailboxChangeSubmission submission,
        ILocalEmailStateStore states,
        IWorkLeaseStore leases,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(retentionReader);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(custody);
        ArgumentNullException.ThrowIfNull(agedMail);
        ArgumentNullException.ThrowIfNull(deleteDispositions);
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(leases);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.retentionReader = retentionReader;
        this.sessions = sessions;
        this.custody = custody;
        this.agedMail = agedMail;
        this.deleteDispositions = deleteDispositions;
        this.mappings = mappings;
        this.submission = submission;
        this.states = states;
        this.leases = leases;
        this.timeProvider = timeProvider;
    }

    /// <summary>Retires one night's share of one account's mail past its age.</summary>
    /// <param name="account">The account.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>How many messages this run retired or recorded a delete for.</returns>
    public async Task<int> RetireAsync(MailAccountId account, CancellationToken cancellationToken)
    {
        var retention = this.retentionReader.GetMessageRetention(account);

        if (!retention.IsEnabled
            || this.TryReadDisposition(account) is not { } disposition
            || !await this.EveryReplicaKnowsRetentionAsync(cancellationToken))
        {
            return 0;
        }

        var now = this.timeProvider.GetUtcNow();
        var receivedBefore = now - retention.After;
        var requester = MailboxMutationRequester.Retention(DateOnly.FromDateTime(now.UtcDateTime));
        var retired = 0;
        var read = 0;
        StoredEmailId? resumeAfter = null;

        while (read < MaximumMessagesReadPerNight)
        {
            var limit = Math.Min(MessagesRetiredPerTransaction, MaximumMessagesReadPerNight - read);

            await using var session = await this.sessions.BeginSessionAsync(cancellationToken);

            var phase = (await this.custody.ReadAsync(session, account, cancellationToken))?.Phase;

            if (phase is not (MailAccountCustodyPhase.Mirrored or MailAccountCustodyPhase.Held))
            {
                return retired;
            }

            var aged = await this.agedMail.ReadAsync(session, account, retention, receivedBefore, resumeAfter, limit, cancellationToken);
            var applied = new List<AppliedMailboxChange>();
            var taken = 0;

            foreach (var mail in aged)
            {
                var server = phase is MailAccountCustodyPhase.Mirrored && !this.IsScheduled(account, mail)
                    ? AuthoredDeleteServerDisposition.LeaveOnServer
                    : disposition.Server;

                if (await this.RetireAsync(session, account, mail, requester, disposition.Local, server, applied, cancellationToken))
                {
                    taken++;
                }
            }

            if (await session.CommitAsync(cancellationToken) is not PersistenceCommitResult.Committed)
            {
                return retired;
            }

            applied.ForEach(this.submission.Announce);
            retired += taken;
            read += aged.Count;

            if (aged.Count < limit)
            {
                return retired;
            }

            resumeAfter = aged[^1].Email;
        }

        return retired;
    }

    private async Task<bool> RetireAsync(
        IPersistenceSession session,
        MailAccountId account,
        AgedStoredMail mail,
        MailboxMutationRequester requester,
        AuthoredDeleteEmailDisposition local,
        AuthoredDeleteServerDisposition server,
        List<AppliedMailboxChange> applied,
        CancellationToken cancellationToken)
    {
        if (mail.Occurrence is not { } occurrence)
        {
            await this.states.EraseAsync(session, account, mail.Email, cancellationToken);
            applied.Add(new AppliedMailboxChange(account, mail.Folder, mail.Email, Flags: null));

            return true;
        }

        var submitted = await this.submission.SubmitAsync(
            session,
            MailboxMutationRequest.Delete(mail.Email, occurrence, requester, local, server),
            destination: null,
            heldUntil: null,
            cancellationToken);

        if (submitted.Change is { } change)
        {
            applied.Add(change);
        }

        return submitted.Outcome is MailboxChangeSubmissionOutcome.Recorded or MailboxChangeSubmissionOutcome.Applied;
    }

    private bool IsScheduled(MailAccountId account, AgedStoredMail mail) =>
        this.mappings.FindFolderNamed(account, mail.SourceFolder)?.Participation.IsSynchronized is true;

    /// <summary>Reads what the account's deletes do, or nothing where a reload has stopped declaring it.</summary>
    private (AuthoredDeleteEmailDisposition Local, AuthoredDeleteServerDisposition Server)? TryReadDisposition(MailAccountId account)
    {
        try
        {
            return (
                this.deleteDispositions.GetAuthoredDeleteDisposition(account),
                this.deleteDispositions.GetAuthoredDeleteServerDisposition(account));
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Answers whether every live work lease names a build that can read a record retention asked for.</summary>
    /// <remarks>A page of leases that fills its bound answers no, since an older build's lease may be in the part not read.</remarks>
    private async Task<bool> EveryReplicaKnowsRetentionAsync(CancellationToken cancellationToken)
    {
        var held = await this.leases.ReadEveryHeldAsync(MaximumInspectedLeases, cancellationToken);

        return held.Count < MaximumInspectedLeases && held.All(static lease => lease.Holder.KnowsRetentionRequester);
    }
}
