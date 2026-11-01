// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Delivery.Receipts;

namespace MailFathom.Application.Emails.Summaries;

/// <summary>Carries what a report said about one recipient it named.</summary>
/// <param name="Address">The address its <c>Final-Recipient</c> field named.</param>
/// <param name="Outcome">The delivery report's <c>Action</c>, or the read receipt's disposition type.</param>
public sealed record EmailReportRecipient(string Address, ReceiptOutcome Outcome);
