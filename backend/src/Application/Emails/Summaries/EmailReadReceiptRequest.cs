// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Emails.Summaries;

/// <summary>Carries a received email's request for a read receipt, and whether this account answered it.</summary>
/// <param name="Address">The address a receipt would go to.</param>
/// <param name="NamesAnotherAddress">Whether that address is neither the message's <c>Return-Path</c> nor its <c>From</c>.</param>
/// <param name="SentAt">When this account sent the receipt, or <see langword="null" /> while it has sent none.</param>
public sealed record EmailReadReceiptRequest(string Address, bool NamesAnotherAddress, DateTimeOffset? SentAt);
