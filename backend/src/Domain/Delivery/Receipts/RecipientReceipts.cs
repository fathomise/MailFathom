// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Delivery.Receipts;

/// <summary>Carries what came back about one recipient of the account's own message.</summary>
/// <param name="Address">The recipient's address, as the message was sent to it.</param>
/// <param name="Delivery">What the newest standing delivery report said, or <see langword="null" /> where none came back.</param>
/// <param name="Read">What a read receipt said, or <see langword="null" /> where none came back.</param>
public sealed record RecipientReceipts(string Address, RecordedReceiptOutcome? Delivery, RecordedReceiptOutcome? Read);
