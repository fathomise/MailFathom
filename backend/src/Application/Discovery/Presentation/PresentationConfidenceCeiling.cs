// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Discovery.Presentation;

/// <summary>Caps the confidence a producer reported for an answer at what the answer's own sources allow.</summary>
/// <remarks>
/// <para>
/// The cap is the whole definition of the value, and it is applied rather than asked for: a model reporting a settled
/// answer over a contradiction, over sources that are all behind, or over no source at all is reporting its own fluency.
/// Every producer that composes an answer block reads its band through this one rule, so an answer means the same
/// confidence whichever surface composed it.
/// </para>
/// <para>
/// A <see cref="PresentationSupport.Cited" /> answer keeps the band it reported. The verdict already says nothing held
/// the answer against its sources, and the band says something else — how much inference the answer takes over what it
/// read — so lowering one because of the other would say the same thing twice and lose what the band was for.
/// </para>
/// </remarks>
public static class PresentationConfidenceCeiling
{
    /// <summary>Reads the band a model reported as a word and caps it at what the support beneath it allows.</summary>
    /// <param name="reported">The word the model reported — <c>high</c>, <c>moderate</c>, or <c>low</c> in any case — or <see langword="null" /> where it reported none.</param>
    /// <param name="support">What the correspondence does for the answer.</param>
    /// <returns>The reported band, or a lower one where the support cannot carry it.</returns>
    /// <remarks>
    /// A word that names no band is read as moderate rather than as high, so a malformed or missing report never
    /// arrives more confident than a well-formed one.
    /// </remarks>
    public static PresentationConfidence Cap(string? reported, PresentationSupport support) =>
        Cap(
            reported?.Trim().ToUpperInvariant() switch
            {
                "HIGH" => PresentationConfidence.High,
                "LOW" => PresentationConfidence.Low,
                _ => PresentationConfidence.Moderate,
            },
            support);

    /// <summary>Caps a reported band at what the support beneath it allows.</summary>
    /// <param name="reported">The band the producer reported.</param>
    /// <param name="support">What the correspondence does for the answer.</param>
    /// <returns>The reported band, or a lower one where the support cannot carry it.</returns>
    public static PresentationConfidence Cap(PresentationConfidence reported, PresentationSupport support) =>
        support switch
        {
            PresentationSupport.Unsupported => PresentationConfidence.Low,
            PresentationSupport.Conflicting or PresentationSupport.Stale =>
                reported is PresentationConfidence.High ? PresentationConfidence.Moderate : reported,
            _ => reported,
        };
}
