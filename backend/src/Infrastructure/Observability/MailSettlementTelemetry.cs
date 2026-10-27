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

    private readonly Counter<long> settledCount;

    /// <summary>Initializes the counter on the process's meter.</summary>
    public MailSettlementTelemetry()
    {
        this.settledCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mail.settlements",
            unit: "{message}",
            description: "Arriving messages made readable, by whether everything that decides their placement finished or they were released after waiting.");
    }

    /// <inheritdoc />
    public void RecordSettled(MailSettlement settlement) =>
        this.settledCount.Add(1, new TagList { { SettlementTagName, TagOf(settlement) } });

    private static string TagOf(MailSettlement settlement) => settlement switch
    {
        MailSettlement.ReleasedAsUnclassifiable => "released_as_unclassifiable",
        MailSettlement.ReleasedWithoutVerdict => "released_without_verdict",
        MailSettlement.ReleasedAfterWaiting => "released_after_waiting",
        _ => "settled",
    };
}
