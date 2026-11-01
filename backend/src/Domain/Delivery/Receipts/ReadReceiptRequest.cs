// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Delivery.Receipts;

/// <summary>Carries a received message's request for a read receipt (RFC 8098).</summary>
/// <param name="Address">The address its <c>Disposition-Notification-To</c> names, which is where a receipt would go.</param>
/// <param name="NamesAnotherAddress">
/// Whether that address is neither the message's <c>Return-Path</c> nor its <c>From</c>. RFC 8098 asks a mail client to
/// make that visible before it answers, because a request naming a third party is how a receipt is turned into a
/// tracking signal sent somewhere the reader never wrote to.
/// </param>
public sealed record ReadReceiptRequest(string Address, bool NamesAnotherAddress);
