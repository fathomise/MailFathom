// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Spam;

namespace MailFathom.Application.Spam.Signals;

/// <summary>What the deterministic stage concluded from a message's own headers and its folder.</summary>
/// <param name="Verdict">The verdict the observed facts reach.</param>
/// <param name="Assessment">The score and threshold when one source carried both, otherwise <see langword="null" />.</param>
/// <param name="Signals">Every fact observed, whether or not it moved the verdict.</param>
/// <remarks>
/// The signals are the whole point of the stage and are returned even when the verdict is
/// <see cref="SpamVerdict.Undetermined" />: an operator diagnosing a classification needs to see that DKIM failed and
/// that no provider wrote a verdict, which is a different situation from a message that carried nothing at all.
/// </remarks>
public sealed record DeterministicSpamReading(
    SpamVerdict Verdict,
    SpamAssessment? Assessment,
    IReadOnlyList<SpamSignal> Signals)
{
    /// <summary>Gets the reading of a message no stage read, which is where the floor starts from when classification does not reach it.</summary>
    public static DeterministicSpamReading Nothing { get; } = new(SpamVerdict.Undetermined, Assessment: null, []);
}
