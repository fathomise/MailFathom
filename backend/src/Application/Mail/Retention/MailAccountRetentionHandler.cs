// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

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
/// The conversation step runs for every account whatever its retention says, because the emptied conversations it
/// removes are left behind by every kind of erasure. It writes the database and reaches no mail server, so it is the
/// same in every custody phase.
/// </para>
/// <para>
/// Each batch is a transaction of its own, so a conversation's row locks are held for moments rather than for the whole
/// run, and what the nightly bound leaves is reached on the next occasion.
/// </para>
/// </remarks>
public sealed class MailAccountRetentionHandler : IJobHandler
{
    /// <summary>The most emptied conversations one account has removed in one night.</summary>
    public const int MaximumConversationsRemovedPerNight = 10_000;

    /// <summary>How many emptied conversations one transaction removes.</summary>
    public const int ConversationsRemovedPerTransaction = 500;

    private readonly IMailRetentionAccounts accounts;
    private readonly IPersistenceSessionFactory sessions;
    private readonly IEmailThreadStore threads;
    private readonly IMailRetentionTelemetry telemetry;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the handler.</summary>
    /// <param name="accounts">Says whether the account has been withheld since the walk handed it out.</param>
    /// <param name="sessions">Begins the transaction of each batch.</param>
    /// <param name="threads">Removes the emptied conversations.</param>
    /// <param name="telemetry">Publishes what the run did.</param>
    /// <param name="timeProvider">Measures the run.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public MailAccountRetentionHandler(
        IMailRetentionAccounts accounts,
        IPersistenceSessionFactory sessions,
        IEmailThreadStore threads,
        IMailRetentionTelemetry telemetry,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(threads);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.accounts = accounts;
        this.sessions = sessions;
        this.threads = threads;
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
        var removed = await this.RemoveEmptiedConversationsAsync(run.Account, cancellationToken);

        this.telemetry.RecordAccountRun(removed, this.timeProvider.GetElapsedTime(startedAt));
    }

    private async Task<int> RemoveEmptiedConversationsAsync(MailAccountId account, CancellationToken cancellationToken)
    {
        var removed = 0;

        while (removed < MaximumConversationsRemovedPerNight)
        {
            var limit = Math.Min(ConversationsRemovedPerTransaction, MaximumConversationsRemovedPerNight - removed);

            await using var session = await this.sessions.BeginSessionAsync(cancellationToken);

            var inBatch = await this.threads.RemoveEmptiedAsync(session, account, limit, cancellationToken);

            if (await session.CommitAsync(cancellationToken) is not PersistenceCommitResult.Committed)
            {
                return removed;
            }

            removed += inBatch;

            if (inBatch < limit)
            {
                return removed;
            }
        }

        return removed;
    }
}
