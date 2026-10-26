// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MailFathom.Domain.Spam;

/// <summary>Identifies the terms a verdict was reached under, as a digest of the deciding stage's terms.</summary>
/// <remarks>
/// <para>
/// Derived rather than declared, for the reason a rule set's revision is: an authored version key can be forgotten, and
/// a change to the terms that leaves the key alone is exactly the case a record has to be able to tell apart. It is what
/// makes "this message was already decided under the terms in force now" a question somebody can ask of an
/// existing record, which is what a run over a whole mailbox skips mail on.
/// </para>
/// <para>
/// The digest covers only what changes a verdict or a subcategory. The deterministic stage reads what a message carried
/// and nothing an operator configures moves its answer, so its terms are its name and the facts it reads — the junk
/// keywords among them, so a change to what it reads is a change of terms and a run decides existing mail once more. The
/// model stage's are whether it
/// is consulted, whether it names subcategories of deterministic spam, and its threshold; a setting that moves an answer
/// joins the canonical form, and every record decided before it then reads as decided under other terms. Leaving the
/// model unconsulted keeps the deterministic identity, so mail already decided without it is not decided again. The scanned folder
/// scope is deliberately outside it — adding a folder changes which mail is classified and not what any classification
/// would have concluded, so including it would force a rescan of mail nothing about the decision has moved for.
/// </para>
/// <para>
/// It carries nothing derived from a message, so a record naming a profile and a log line reporting one hold nothing
/// personal.
/// </para>
/// </remarks>
public readonly record struct SpamClassificationProfile
{
    /// <summary>How many hexadecimal characters of the digest the identity keeps.</summary>
    /// <remarks>
    /// Twelve characters are forty-eight bits, far more than enough to tell apart the handful of settings one deployment
    /// passes through and short enough to read in a log line without being elided.
    /// </remarks>
    public const int LengthInCharacters = 12;

    /// <summary>The canonical form of what the deterministic stage reads: the headers, the folder, and the junk keywords.</summary>
    private const string DeterministicTerms = "deterministic;junk-keywords";

    private readonly string? value;

    private SpamClassificationProfile(string value) => this.value = value;

    /// <summary>Gets whether this value names a profile rather than the unusable struct default.</summary>
    /// <remarks>
    /// A classification recorded before the profile became part of the record carries none, which is what an unspecified
    /// value means when it is read back. A run treats such a record as decided under terms it cannot compare, and
    /// therefore as mail to classify again rather than as mail to skip.
    /// </remarks>
    public bool IsSpecified => this.value is not null;

    /// <summary>Gets the identity, as lowercase hexadecimal.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a profile.</exception>
    public string Value => this.value
        ?? throw new InvalidOperationException("The value is the default of the struct and does not name a profile.");

    /// <summary>Derives the identity of the terms a classification is reached under when no model is consulted.</summary>
    /// <returns>The profile of the deterministic stage alone.</returns>
    public static SpamClassificationProfile Create() => FromCanonicalForm(DeterministicTerms);

    /// <summary>Derives the identity of the terms a classification is reached under when the model stage is consulted.</summary>
    /// <param name="namesSubcategoriesOfDeterministicSpam">Whether the model also names the subcategory of a verdict the deterministic stage reached.</param>
    /// <param name="threshold">The probability at or above which the model's judgement counts as spam.</param>
    /// <returns>The profile, which differs from the deterministic one and from every other combination of these terms.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="threshold" /> is not a probability above zero.</exception>
    /// <remarks>
    /// Each term moves an answer: consulting the model decides mail the deterministic stage could not, naming
    /// subcategories reaches mail it already called spam, and the threshold moves where the model's probability turns into
    /// a verdict. The threshold is written in its round-trip form, so two thresholds that differ at all are two profiles.
    /// </remarks>
    public static SpamClassificationProfile Create(bool namesSubcategoriesOfDeterministicSpam, double threshold)
    {
        if (!double.IsFinite(threshold) || threshold is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(threshold),
                threshold,
                "A model threshold is a probability above zero and at most one.");
        }

        return FromCanonicalForm(string.Create(
            CultureInfo.InvariantCulture,
            $"{DeterministicTerms};model;subcategories-of-deterministic-spam={(namesSubcategoriesOfDeterministicSpam ? "yes" : "no")};threshold={threshold:R}"));
    }

    /// <summary>Reads back a profile this system derived earlier and recorded.</summary>
    /// <param name="value">The recorded identity.</param>
    /// <returns>The profile, which compares equal to a freshly derived one of the same terms.</returns>
    /// <exception cref="ArgumentException">Thrown when the value is not an identity this type could have produced.</exception>
    /// <remarks>
    /// The shape is checked rather than trusted, because a value that is not one this type produces would compare unequal
    /// to every profile and silently make every classified message look like mail to score again.
    /// </remarks>
    public static SpamClassificationProfile Restore(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length != LengthInCharacters
            || !value.All(static character =>
                char.IsAsciiDigit(character) || (char.IsAsciiLetterLower(character) && character <= 'f')))
        {
            throw new ArgumentException(
                $"A spam classification profile is exactly {LengthInCharacters} lowercase hexadecimal characters.",
                nameof(value));
        }

        return new SpamClassificationProfile(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.value ?? "(unspecified)";

    private static SpamClassificationProfile FromCanonicalForm(string canonicalForm) => new(
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalForm)))[..LengthInCharacters]);
}
