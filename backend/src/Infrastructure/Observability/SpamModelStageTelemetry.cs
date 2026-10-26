// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailFathom.Application.Spam.Model;
using MailFathom.Common.Observability;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Counts every consultation of the spam model stage by its job and by how it ended.</summary>
/// <remarks>
/// Each value is a fixed tag written here, so a renamed member cannot silently rename a series, and none of them says
/// anything about the message: the verdict, the subcategory, and the probability stay out of every counter.
/// </remarks>
public sealed class SpamModelStageTelemetry : ISpamModelStageTelemetry
{
    private const string JobTagName = "mailfathom.spam.model_stage.job";
    private const string OutcomeTagName = "mailfathom.spam.model_stage.outcome";

    private readonly Counter<long> consultationCount = Telemetry.Meter.CreateCounter<long>(
        "mailfathom.spam.model_stage.consultations",
        unit: "{consultation}",
        description: "Consultations of the spam model stage, by job and by how each ended; every outcome but answered left the deterministic verdict in place.");

    /// <inheritdoc />
    public void RecordConsultation(SpamModelJob job, SpamModelOutcome outcome) =>
        this.consultationCount.Add(1, new TagList { { JobTagName, TagOf(job) }, { OutcomeTagName, TagOf(outcome) } });

    private static string TagOf(SpamModelJob job) => job switch
    {
        SpamModelJob.DecideVerdict => "decide_verdict",
        SpamModelJob.NameSubcategory => "name_subcategory",
        _ => "unknown",
    };

    private static string TagOf(SpamModelOutcome outcome) => outcome switch
    {
        SpamModelOutcome.Answered => "answered",
        SpamModelOutcome.NotActivated => "not_activated",
        SpamModelOutcome.BudgetExhausted => "budget_exhausted",
        SpamModelOutcome.ProviderUnavailable => "provider_unavailable",
        SpamModelOutcome.AnswerUnreadable => "answer_unreadable",
        SpamModelOutcome.InputTooLarge => "input_too_large",
        SpamModelOutcome.ScannerUnavailable => "scanner_unavailable",
        _ => "unknown",
    };
}
