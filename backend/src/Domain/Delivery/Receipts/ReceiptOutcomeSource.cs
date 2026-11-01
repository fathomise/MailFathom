// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Delivery.Receipts;

/// <summary>States where a recorded outcome came from.</summary>
/// <remarks>
/// Recorded beside the outcome rather than implied by it, because a reader is owed the difference: a report is a message
/// anybody could have written, so what it says is information rather than proof.
/// </remarks>
public enum ReceiptOutcomeSource
{
    /// <summary>A report that arrived as mail and named the account's own message.</summary>
    Report = 0,
}
