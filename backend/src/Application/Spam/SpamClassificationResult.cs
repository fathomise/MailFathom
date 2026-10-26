// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Spam;

namespace MailFathom.Application.Spam;

/// <summary>What one attempt to classify an occurrence produced.</summary>
/// <param name="Outcome">How the attempt ended.</param>
/// <param name="Classification">
/// What was recorded, present exactly when the outcome is <see cref="SpamClassificationOutcome.Classified" />, or the
/// person's verdict that stands, present exactly when it is <see cref="SpamClassificationOutcome.PersonsVerdictKept" />.
/// </param>
/// <remarks>
/// The classification travels back with the outcome so a caller that has to act on the verdict — filing junk on the
/// server, or holding an occurrence back from embedding — does not read it again from the store it was just written to.
/// It is derived data about mail and therefore not loggable; the outcome is.
/// </remarks>
public sealed record SpamClassificationResult(
    SpamClassificationOutcome Outcome,
    SpamClassification? Classification)
{
    /// <summary>Records that nothing was classified, and why.</summary>
    /// <param name="outcome">The reason.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="outcome" /> is <see cref="SpamClassificationOutcome.Classified" /> or
    /// <see cref="SpamClassificationOutcome.PersonsVerdictKept" />, each of which carries a classification instead.
    /// </exception>
    public static SpamClassificationResult NotClassified(SpamClassificationOutcome outcome) =>
        outcome is SpamClassificationOutcome.Classified or SpamClassificationOutcome.PersonsVerdictKept
            ? throw new ArgumentOutOfRangeException(
                nameof(outcome),
                outcome,
                "A result without a classification carries neither the classified outcome nor the person's kept verdict.")
            : new SpamClassificationResult(outcome, Classification: null);

    /// <summary>Records that the person's own verdict stands and nothing was classified over it.</summary>
    /// <param name="classification">The person's verdict, as it is recorded.</param>
    /// <returns>The result, carrying the person's verdict for the caller to act on.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="classification" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the classification is not the person's.</exception>
    public static SpamClassificationResult PersonsVerdictKept(SpamClassification classification)
    {
        ArgumentNullException.ThrowIfNull(classification);

        return classification.DecidedBy is SpamClassificationStage.Person
            ? new SpamClassificationResult(SpamClassificationOutcome.PersonsVerdictKept, classification)
            : throw new ArgumentException("Only the person's own verdict is kept over a classification.", nameof(classification));
    }

    /// <summary>Records what was classified.</summary>
    /// <param name="classification">The verdict and the facts it rests on.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="classification" /> is <see langword="null" />.</exception>
    public static SpamClassificationResult Classified(SpamClassification classification)
    {
        ArgumentNullException.ThrowIfNull(classification);

        return new SpamClassificationResult(SpamClassificationOutcome.Classified, classification);
    }
}
