// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Contacts;

/// <summary>Holds one phone number a user wrote down for a person, and which of their phones it reaches.</summary>
/// <remarks>
/// <para>
/// The number is kept as the user typed it rather than normalized to E.164, because nothing here compares, deduplicates,
/// or dials one: it is read back to the person who wrote it and offered as a link. A normalization would need a
/// numbering-plan library and a default region to guess with, and would rewrite a number somebody wrote in the form
/// they read it in. It becomes worth having when numbers are matched against one another or exported to vCard.
/// </para>
/// <para>
/// The number is personal data about a third party. It is never logged, never a metric dimension, and never written into
/// a failure message.
/// </para>
/// </remarks>
public readonly record struct ContactPhoneNumber
{
    /// <summary>The greatest length a number may carry.</summary>
    /// <remarks>
    /// E.164 allows fifteen digits; the rest is room for the plus sign and the spaces, dashes, dots, and brackets a
    /// person groups the digits with.
    /// </remarks>
    public const int MaximumLength = 32;

    private ContactPhoneNumber(string number, ContactPhoneLabel label)
    {
        this.Number = number;
        this.Label = label;
    }

    /// <summary>Gets the number as the user wrote it, trimmed of surrounding whitespace.</summary>
    public string Number { get; }

    /// <summary>Gets which of the person's phones the number reaches.</summary>
    public ContactPhoneLabel Label { get; }

    /// <summary>Creates a number from text a user supplied.</summary>
    /// <param name="number">The number to record.</param>
    /// <param name="label">Which of the person's phones it reaches.</param>
    /// <returns>A validated number, trimmed and otherwise as written.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="number" /> is blank, longer than <see cref="MaximumLength" />, holds no digit, or holds a character other than a digit, <c>+</c>, a space, <c>-</c>, <c>(</c>, <c>)</c>, or <c>.</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="label" /> names no declared value.</exception>
    public static ContactPhoneNumber Create(string number, ContactPhoneLabel label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);

        if (!Enum.IsDefined(label))
        {
            throw new ArgumentOutOfRangeException(nameof(label), "A phone label must name a declared value.");
        }

        var trimmed = number.Trim();

        if (!IsWellFormed(trimmed))
        {
            throw new ArgumentException(
                $"A phone number is at most {MaximumLength} characters of digits, '+', spaces, '-', '(', ')', and '.', with at least one digit.",
                nameof(number));
        }

        return new ContactPhoneNumber(trimmed, label);
    }

    /// <summary>Answers whether trimmed text is a number this record admits.</summary>
    /// <param name="trimmed">The text to judge, already trimmed.</param>
    /// <returns><see langword="true" /> when it fits the length and character rules and holds a digit.</returns>
    public static bool IsWellFormed(string trimmed) =>
        trimmed.Length is > 0 and <= MaximumLength
        && trimmed.Any(char.IsAsciiDigit)
        && trimmed.All(character => char.IsAsciiDigit(character) || character is '+' or ' ' or '-' or '(' or ')' or '.');

    /// <inheritdoc />
    public override string ToString() => this.Number;
}
