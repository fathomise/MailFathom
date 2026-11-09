// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailFathom.Application.Categories;
using MailFathom.Application.Decisions;
using MailFathom.Common.Observability;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Publishes how each consultation of categorization ended, carrying nothing about the message or the category.</summary>
public sealed class EmailCategorizationTelemetry : IEmailCategorizationTelemetry
{
    private const string OutcomeTagName = "mailfathom.email.categorization.outcome";

    private readonly Counter<long> consultationCount = Telemetry.Meter.CreateCounter<long>(
        "mailfathom.email.categorization.consultations",
        unit: "{consultation}",
        description: "Consultations of email categorization, by how each ended; every outcome but answered left the message with no category.");

    /// <inheritdoc />
    public void RecordConsultation(DecisionModelOutcome outcome) =>
        this.consultationCount.Add(1, new TagList { { OutcomeTagName, DecisionModelOutcomeTags.TagOf(outcome) } });
}
