// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Accounts.Custody;
using MailFathom.Application.Emails.Threads;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Mail.Retention;

/// <summary>Runs one account's retention steps for one night.</summary>
/// <remarks>
/// <para>
/// Tombstones go first, so a conversation their erasure empties is removed the same night. That step runs only for an
/// account that asked for it, in <c>Mirrored</c> and <c>Held</c>, and in neither reaches a mail server. It does not run
/// while <c>Restoring</c>, when the mailbox is being put back and nothing of it should be thrown away underneath that.
/// </para>
/// <para>
/// The conversation step runs for every account whatever its retention says, because the emptied conversations it
/// removes are left behind by every kind of erasure. It writes the database and reaches no mail server, so it is the
/// same in every custody phase.
/// </para>
/// <para>
/// Each batch is a transaction of its own, so row locks are held for moments rather than for the whole run, and what the
/// nightly bound leaves is reached on the next occasion. A failure leaves what committed committed, and the next
/// occasion repeats the rest.
/// </para>
/// </remarks>
public sealed class MailAccountRetentionHandler : IJobHandler
{
    /// <summary>The most tombstones one account has erased in one night.</summary>
    public const int MaximumTombstonesErasedPerNight = 10_000;

    /// <summary>How many tombstones one transaction erases.</summary>
    public const int TombstonesErasedPerTransaction = 100;

    /// <summary>The most emptied conversations one account has removed in one night.</summary>
    public const int MaximumConversationsRemovedPerNight = 10_000;

    /// <summary>How many emptied conversations one transaction removes.</summary>
    public const int ConversationsRemovedPerTransaction = 500;

    private readonly IMailRetentionAccounts accounts;
    private readonly IPersistenceSessionFactory sessions;
    private readonly IEmailThreadStore threads;
    private readonly IMailTombstoneRetentionReader tombstoneRetention;
    private readonly IMailAccountCustodyStore custody;
    private readonly IStoredTombstoneEraser tombstones;
    private readonly IMailRetentionTelemetry telemetry;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the handler.</summary>
    /// <param name="accounts">Says whether the account has been withheld since the walk handed it out.</param>
    /// <param name="sessions">Begins the transaction of each batch.</param>
    /// <param name="threads">Removes the emptied conversations.</param>
    /// <param name="tombstoneRetention">Reads how long the account keeps its tombstones.</param>
    /// <param name="custody">Reads which copy of the account's mailbox is the truth.</param>
    /// <param name="tombstones">Erases the tombstones past that age.</param>
    /// <param name="telemetry">Publishes what the run did.</param>
    /// <param name="timeProvider">Measures the run and a tombstone's age.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public MailAccountRetentionHandler(
        IMailRetentionAccounts accounts,
        IPersistenceSessionFactory sessions,
        IEmailThreadStore threads,
        IMailTombstoneRetentionReader tombstoneRetention,
        IMailAccountCustodyStore custody,
        IStoredTombstoneEraser tombstones,
        IMailRetentionTelemetry telemetry,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(threads);
        ArgumentNullException.ThrowIfNull(tombstoneRetention);
        ArgumentNullException.ThrowIfNull(custody);
        ArgumentNullException.ThrowIfNull(tombstones);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.accounts = accounts;
        this.sessions = sessions;
        this.threads = threads;
        this.tombstoneRetention = tombstoneRetention;
        this.custody = custody;
        this.tombstones = tombstones;
        this.telemetry = telemetry;
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public JobType JobType => JobType.RetainMailAccount;

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Thrown when the payload is not the contract this job type names.</exception>
    public async Task RunAsync(IJobPayload payload, CancellationToken cancellationToken)
    {
        if (payload is not RetainMailAccountJobPayload run)
        {
            throw new ArgumentException(
                $"A '{JobType.RetainMailAccount}' job carries a payload naming one mail account.",
                nameof(payload));
        }

        if (this.accounts.IsWithheld(run.Account))
        {
            return;
        }

        var startedAt = this.timeProvider.GetTimestamp();
        var erased = await this.EraseAgedTombstonesAsync(run.Account, cancellationToken);
        var removed = await this.TakeInBatchesAsync(
            (session, limit) => this.threads.RemoveEmptiedAsync(session, run.Account, limit, cancellationToken),
            ConversationsRemovedPerTransaction,
            MaximumConversationsRemovedPerNight,
            cancellationToken);

        this.telemetry.RecordAccountRun(erased, removed, this.timeProvider.GetElapsedTime(startedAt));
    }

    private async Task<int> EraseAgedTombstonesAsync(MailAccountId account, CancellationToken cancellationToken)
    {
        var retention = this.tombstoneRetention.GetTombstoneRetention(account);

        if (!retention.IsEnabled)
        {
            return 0;
        }

        var tombstonedBefore = this.timeProvider.GetUtcNow() - retention.After;

        return await this.TakeInBatchesAsync(
            (session, limit) => this.EraseBatchAsync(session, account, retention, tombstonedBefore, limit, cancellationToken),
            TombstonesErasedPerTransaction,
            MaximumTombstonesErasedPerNight,
            cancellationToken);
    }

    /// <summary>Erases one batch under the custody read in that batch's own transaction.</summary>
    /// <remarks>Read per batch because a run spans many of them, and an account can start being restored or held in between.</remarks>
    private async Task<int> EraseBatchAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailTombstoneRetention retention,
        DateTimeOffset tombstonedBefore,
        int limit,
        CancellationToken cancellationToken)
    {
        var state = await this.custody.ReadAsync(session, account, cancellationToken);

        if (state?.Phase is not (MailAccountCustodyPhase.Mirrored or MailAccountCustodyPhase.Held))
        {
            return 0;
        }

        return await this.tombstones.EraseAsync(
            session,
            account,
            retention,
            tombstonedBefore,
            isSourceDrained: state.Phase is MailAccountCustodyPhase.Held,
            limit,
            cancellationToken);
    }

    private async Task<int> TakeInBatchesAsync(
        Func<IPersistenceSession, int, Task<int>> takeBatchAsync,
        int perTransaction,
        int perNight,
        CancellationToken cancellationToken)
    {
        var taken = 0;

        while (taken < perNight)
        {
            var limit = Math.Min(perTransaction, perNight - taken);

            await using var session = await this.sessions.BeginSessionAsync(cancellationToken);

            var inBatch = await takeBatchAsync(session, limit);

            if (await session.CommitAsync(cancellationToken) is not PersistenceCommitResult.Committed)
            {
                return taken;
            }

            taken += inBatch;

            if (inBatch < limit)
            {
                return taken;
            }
        }

        return taken;
    }
}
