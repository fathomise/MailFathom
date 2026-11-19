// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailFathom.Application.Emails.Settlement;
using MailFathom.Common.Observability;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Publishes how arriving messages settled as one counter tagged by settlement.</summary>
public sealed class MailSettlementTelemetry : IMailSettlementTelemetry
{
    private const string SettlementTagName = "mailfathom.mail.settlement";

    private const string FollowUpTagName = "mailfathom.mail.settlement.follow_up";

    private readonly Counter<long> settledCount;
    private readonly Counter<long> followUpFailureCount;

    /// <summary>Initializes the counter on the process's meter.</summary>
    public MailSettlementTelemetry()
    {
        this.settledCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mail.settlements",
            unit: "{message}",
            description: "Arriving messages made readable, by whether everything that decides their placement finished or they were released after waiting.");
        this.followUpFailureCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mail.settlement.follow_up_failures",
            unit: "{batch}",
            description: "Batches of settled messages whose automatic reply or closing of awaited replies failed, which is not retried.");
    }

    /// <inheritdoc />
    public void RecordSettled(MailSettlement settlement) =>
        this.settledCount.Add(1, new TagList { { SettlementTagName, TagOf(settlement) } });

    /// <inheritdoc />
    public void RecordFollowUpFailed(MailSettlementFollowUp followUp) =>
        this.followUpFailureCount.Add(
            1,
            new TagList { { FollowUpTagName, followUp == MailSettlementFollowUp.AutomaticReply ? "automatic_reply" : "awaited_replies" } });

    private static string TagOf(MailSettlement settlement) => settlement switch
    {
        MailSettlement.ReleasedAsUnclassifiable => "released_as_unclassifiable",
        MailSettlement.ReleasedWithoutVerdict => "released_without_verdict",
        MailSettlement.ReleasedAfterWaiting => "released_after_waiting",
        _ => "settled",
    };
}
