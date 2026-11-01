// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Delivery.Receipts;

namespace MailFathom.Application.Mail.Delivery.Receipts;

/// <summary>Carries what an arriving message said about receipts, decided before the transaction that stores it.</summary>
/// <param name="Report">The report the message is, or <see langword="null" /> where it is none.</param>
/// <param name="ReportedOutgoingEmail">The account's own message the report named, or <see langword="null" /> where it named none.</param>
/// <param name="Outcomes">The outcome to apply to each recipient of that message, which is empty where it named none.</param>
/// <param name="ReadReceiptRequest">The message's request for a read receipt, or <see langword="null" /> where it asked for none or is the account's own.</param>
public sealed record ArrivedReceipts(
    ReceiptReport? Report,
    ReportedOutgoingEmail? ReportedOutgoingEmail,
    IReadOnlyList<RecipientOutcomeUpdate> Outcomes,
    ReadReceiptRequest? ReadReceiptRequest)
{
    /// <summary>Gets the organization's send the message is, delivered to the receiving account, or <see langword="null" /> where it is none.</summary>
    public OrganizationDelivery? OrganizationDelivery { get; init; }
}
