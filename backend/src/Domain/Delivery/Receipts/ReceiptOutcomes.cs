// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Delivery.Receipts;

/// <summary>Holds the rules that decide which report's outcome a recipient keeps.</summary>
public static class ReceiptOutcomes
{
    /// <summary>Names the report an outcome can only have come from.</summary>
    /// <param name="outcome">The outcome.</param>
    /// <returns>The kind of report that states it.</returns>
    public static ReceiptKind KindOf(ReceiptOutcome outcome) =>
        outcome <= ReceiptOutcome.Delayed ? ReceiptKind.Delivery : ReceiptKind.Read;

    /// <summary>Answers whether a newly reported outcome takes the place of the one already recorded.</summary>
    /// <param name="recorded">The outcome the recipient holds for this kind of report.</param>
    /// <param name="reported">The outcome a report that arrived later states.</param>
    /// <returns><see langword="true" /> when the recipient should hold <paramref name="reported" /> from now on.</returns>
    /// <remarks>
    /// Only <see cref="ReceiptOutcome.Delayed" /> is provisional: a server still trying will report again, with a
    /// delivery, a failure, or a further delay. Every other outcome is final, so a later report — a duplicate, a report
    /// that crossed another on the way, or one somebody forged — never replaces it. Reports arrive in no guaranteed order,
    /// which is why the rule reads the outcomes rather than the times.
    /// </remarks>
    public static bool Replaces(ReceiptOutcome recorded, ReceiptOutcome reported) =>
        recorded == ReceiptOutcome.Delayed && KindOf(reported) == ReceiptKind.Delivery;
}
