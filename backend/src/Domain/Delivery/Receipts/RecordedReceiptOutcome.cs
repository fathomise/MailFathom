// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Delivery.Receipts;

/// <summary>Carries what was recorded about one recipient of the account's own message, from one kind of report.</summary>
/// <param name="Outcome">What the report said happened.</param>
/// <param name="ReportedAt">When the report says it was written, or when it was stored where it said nothing usable.</param>
/// <param name="Source">Where the outcome came from.</param>
/// <param name="Status">The RFC 3463 status code a delivery report gave, such as <c>5.1.1</c>, or <see langword="null" /> for a read receipt and a report that gave none.</param>
/// <param name="ReportingServer">The server a delivery report names as having reported it, or <see langword="null" /> where it named none.</param>
/// <remarks>
/// The time a read receipt reports is personal data: it says when a named person read the message. It carries the
/// classification of the message it describes and is erased with it.
/// </remarks>
public sealed record RecordedReceiptOutcome(
    ReceiptOutcome Outcome,
    DateTimeOffset ReportedAt,
    ReceiptOutcomeSource Source,
    string? Status,
    string? ReportingServer)
{
    /// <summary>Gets the kind of report the outcome came from.</summary>
    public ReceiptKind Kind => ReceiptOutcomes.KindOf(this.Outcome);
}
