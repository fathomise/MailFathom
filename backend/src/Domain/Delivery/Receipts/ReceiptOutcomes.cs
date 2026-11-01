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

    /// <summary>Answers whether a newly recorded outcome takes the place of the one already recorded.</summary>
    /// <param name="recorded">The outcome the recipient holds for this kind of report.</param>
    /// <param name="reported">The outcome that arrived later, from a report or from this deployment itself.</param>
    /// <returns><see langword="true" /> when the recipient should hold <paramref name="reported" /> from now on.</returns>
    /// <remarks>
    /// <para>
    /// An outcome this deployment observed between two accounts of one organization
    /// (<see cref="ReceiptOutcomeSource.Organization" />) is proof, so it replaces whatever a report said and nothing
    /// replaces it: neither a report, which anybody could have written, nor a second observation of the same thing.
    /// </para>
    /// <para>
    /// Between reports, only <see cref="ReceiptOutcome.Delayed" /> is provisional: a server still trying will report
    /// again, with a delivery, a failure, or a further delay. Every other outcome is final, so a later report — a
    /// duplicate, a report that crossed another on the way, or one somebody forged — never replaces it. Reports arrive in
    /// no guaranteed order, which is why the rule reads the outcomes rather than the times.
    /// </para>
    /// </remarks>
    public static bool Replaces(RecordedReceiptOutcome recorded, RecordedReceiptOutcome reported)
    {
        ArgumentNullException.ThrowIfNull(recorded);
        ArgumentNullException.ThrowIfNull(reported);

        if (recorded.Source == ReceiptOutcomeSource.Organization)
        {
            return false;
        }

        return reported.Source == ReceiptOutcomeSource.Organization
            || (recorded.Outcome == ReceiptOutcome.Delayed && reported.Kind == ReceiptKind.Delivery);
    }
}
