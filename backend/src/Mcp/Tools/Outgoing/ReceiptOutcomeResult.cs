// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel;
using MailFathom.Domain.Delivery.Receipts;

namespace MailFathom.Mcp.Tools.Outgoing;

/// <summary>Publishes one outcome recorded about one recipient.</summary>
internal sealed record ReceiptOutcomeResult
{
    /// <summary>Gets what the report said.</summary>
    [Description("What the report said happened.")]
    public required ReceiptOutcomeKind Outcome { get; init; }

    /// <summary>Gets when the report says it was written.</summary>
    [Description("When the report says it was written.")]
    public required DateTimeOffset ReportedAt { get; init; }

    /// <summary>Gets where the outcome came from.</summary>
    [Description("Where the outcome came from.")]
    public required ReceiptOutcomeOrigin Source { get; init; }

    /// <summary>Gets the RFC 3463 status code a delivery report gave.</summary>
    [Description("The RFC 3463 status code a delivery report gave, such as 5.1.1, or absent where it gave none.")]
    public string? Status { get; init; }

    /// <summary>Publishes one recorded outcome, or nothing where none was recorded.</summary>
    /// <param name="outcome">The recorded outcome.</param>
    /// <returns>The wire representation, or <see langword="null" /> where <paramref name="outcome" /> is.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the outcome is not one this surface publishes.</exception>
    public static ReceiptOutcomeResult? From(RecordedReceiptOutcome? outcome) => outcome is null
        ? null
        : new ReceiptOutcomeResult
        {
            Outcome = Published(outcome.Outcome),
            ReportedAt = outcome.ReportedAt,
            Source = outcome.Source switch
            {
                ReceiptOutcomeSource.Report => ReceiptOutcomeOrigin.Report,
                ReceiptOutcomeSource.Organization => ReceiptOutcomeOrigin.Organization,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(outcome),
                    outcome.Source,
                    "The receipt outcome source is not one this surface publishes."),
            },
            Status = outcome.Status,
        };

    /// <summary>Reads the published spelling of one outcome, written out so a new outcome has to be given one.</summary>
    private static ReceiptOutcomeKind Published(ReceiptOutcome outcome) => outcome switch
    {
        ReceiptOutcome.Delivered => ReceiptOutcomeKind.Delivered,
        ReceiptOutcome.Relayed => ReceiptOutcomeKind.Relayed,
        ReceiptOutcome.Expanded => ReceiptOutcomeKind.Expanded,
        ReceiptOutcome.Failed => ReceiptOutcomeKind.Failed,
        ReceiptOutcome.Delayed => ReceiptOutcomeKind.Delayed,
        ReceiptOutcome.Displayed => ReceiptOutcomeKind.Displayed,
        ReceiptOutcome.Deleted => ReceiptOutcomeKind.Deleted,
        ReceiptOutcome.Dispatched => ReceiptOutcomeKind.Dispatched,
        ReceiptOutcome.Processed => ReceiptOutcomeKind.Processed,
        _ => throw new ArgumentOutOfRangeException(
            nameof(outcome),
            outcome,
            "The receipt outcome is not one this surface publishes."),
    };
}
