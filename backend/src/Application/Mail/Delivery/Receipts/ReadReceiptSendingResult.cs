// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Mail.Delivery.Receipts;

/// <summary>Carries how a reader's request to send a read receipt ended.</summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="SentAt">When the receipt was sent: now for <see cref="ReadReceiptSendingOutcome.Sent" />, the earlier time for <see cref="ReadReceiptSendingOutcome.AlreadySent" />, and <see langword="null" /> otherwise.</param>
public sealed record ReadReceiptSendingResult(ReadReceiptSendingOutcome Outcome, DateTimeOffset? SentAt)
{
    /// <summary>Answers with a refusal that carries no time.</summary>
    /// <param name="outcome">The refusal.</param>
    /// <returns>The result.</returns>
    public static ReadReceiptSendingResult Refused(ReadReceiptSendingOutcome outcome) => new(outcome, SentAt: null);
}
