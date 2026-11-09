// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.Metrics;
using MailFathom.Application.Mail.Retention;
using MailFathom.Common.Observability;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Publishes what a night of retention did for each account it reached.</summary>
/// <remarks>Counts and durations only: nothing here carries an account, a conversation, or anything read from mail.</remarks>
internal sealed class MailRetentionTelemetry : IMailRetentionTelemetry
{
    private readonly Counter<long> erasedTombstones;
    private readonly Counter<long> removedConversations;
    private readonly Histogram<double> accountRunDuration;

    /// <summary>Initializes the instruments.</summary>
    public MailRetentionTelemetry()
    {
        this.erasedTombstones = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mail_retention.tombstones.erased",
            unit: "{message}",
            description: "Stored messages erased because they had been tombstones for longer than their account keeps them.");
        this.removedConversations = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mail_retention.conversations.removed",
            unit: "{conversation}",
            description: "Conversations removed because no stored message was left in them.");
        this.accountRunDuration = Telemetry.Meter.CreateHistogram<double>(
            "mailfathom.mail_retention.account_run.duration",
            unit: "s",
            description: "How long one account's run of the nightly retention steps took.");
    }

    /// <inheritdoc />
    public void RecordAccountRun(int erasedTombstones, int removedConversations, TimeSpan duration)
    {
        if (erasedTombstones > 0)
        {
            this.erasedTombstones.Add(erasedTombstones);
        }

        if (removedConversations > 0)
        {
            this.removedConversations.Add(removedConversations);
        }

        this.accountRunDuration.Record(duration.TotalSeconds);
    }
}
