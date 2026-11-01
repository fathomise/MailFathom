// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Delivery.Receipts;

/// <summary>Carries what one report said about one recipient it named.</summary>
/// <param name="Recipient">The address the report's <c>Final-Recipient</c> field named.</param>
/// <param name="OriginalRecipient">
/// The address its <c>Original-Recipient</c> field named, or <see langword="null" /> where it wrote none. It is the address
/// the message was sent to where the two differ — an alias or a forward delivered it to another mailbox — so it is the one
/// held against the sent message's recipients first.
/// </param>
/// <param name="Outcome">The <c>Action</c> of a delivery report, or the disposition type of a read receipt.</param>
/// <param name="Status">The RFC 3463 status code a delivery report gave, or <see langword="null" /> where it gave none.</param>
/// <param name="ReportingServer">The server a delivery report names as having reported it, or <see langword="null" /> where it named none.</param>
public sealed record ReceiptReportRecipient(
    string Recipient,
    string? OriginalRecipient,
    ReceiptOutcome Outcome,
    string? Status,
    string? ReportingServer);
