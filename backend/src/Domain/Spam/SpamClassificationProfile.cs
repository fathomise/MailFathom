// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

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
/// The digest covers only what changes a verdict. Today that is the stage that decides, because the deterministic stage
/// reads what a message carried and nothing an operator configures moves its answer; a setting that does move one joins
/// the canonical form, and every record decided before it then reads as decided under other terms. The scanned folder
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

    /// <summary>Derives the identity of the terms a classification is reached under, which this release fixes.</summary>
    /// <returns>The profile every verdict this release reaches is recorded under.</returns>
    public static SpamClassificationProfile Create()
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes("deterministic"));

        return new SpamClassificationProfile(Convert.ToHexStringLower(digest)[..LengthInCharacters]);
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
}
