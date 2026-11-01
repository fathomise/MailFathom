// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Delivery.Receipts;

namespace MailFathom.Application.Mail.Delivery.Receipts;

/// <summary>Carries one outcome a report gives one recipient of the account's own message.</summary>
/// <param name="RecipientOrdinal">The recipient's position in the message's recipient list.</param>
/// <param name="Outcome">What the report said, which the store applies only where it replaces what is recorded.</param>
public sealed record RecipientOutcomeUpdate(int RecipientOrdinal, RecordedReceiptOutcome Outcome);
