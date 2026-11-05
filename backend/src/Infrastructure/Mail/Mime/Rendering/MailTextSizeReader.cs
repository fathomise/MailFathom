// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using MailFathom.Application.EmailContent.Rendering.Document;

namespace MailFathom.Infrastructure.Mail.Mime.Rendering;

/// <summary>Reads the three ways mail writes a text size onto the closed scale the document carries.</summary>
/// <remarks>
/// <para>
/// Every row here is
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0047-what-the-composer-may-write-beyond-its-closed-set-and-how-it-reads-a-formatted-paste.md">ADR 0047</see>'s
/// size table, which the composer's paste reader implements as well; the two are kept in agreement by each being tested
/// against those rows rather than against each other.
/// </para>
/// <para>
/// A length is read without the parent the sender meant, because the reduction resolves no cascade: a point is four
/// thirds of a pixel and a relative length is read against 16px. A zero size is small print rather than nothing, since
/// shrinking text to nothing is how a sender hides it and the scale exists so that what a sender hid is still read.
/// </para>
/// </remarks>
internal static class MailTextSizeReader
{
    private const double PixelsPerPoint = 4.0 / 3.0;
    private const double RelativeBasePixels = 16;

    /// <summary>Reads the value of a <c>font-size</c> declaration.</summary>
    /// <param name="value">The value as the declaration states it, without a qualifier.</param>
    /// <returns>The step, or <see langword="null" /> where the value is no size this reads.</returns>
    internal static MailTextSize? FromDeclaration(string value) =>
        FromKeyword(value.Trim()) ?? FromLength(value.Trim());

    /// <summary>Reads the <c>size</c> attribute of a <c>font</c> element, absolute or relative to the default of 3.</summary>
    /// <param name="value">The attribute as the message wrote it.</param>
    /// <returns>The step, or <see langword="null" /> where the attribute is absent or no size.</returns>
    /// <remarks>
    /// A signed value is relative to 3 and every value is clamped to the seven sizes the attribute has, which is how the
    /// HTML standard reads a legacy font size — so <c>+1</c> is 4 and <c>9</c> is 7.
    /// </remarks>
    internal static MailTextSize? FromFontAttribute(string? value)
    {
        var written = value?.Trim();
        if (string.IsNullOrEmpty(written))
        {
            return null;
        }

        var relative = written[0] is '+' or '-';
        var digits = relative ? written[1..] : written;

        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return null;
        }

        var bounded = Math.Min(number, 7);
        var size = relative ? 3 + (written[0] == '-' ? -bounded : bounded) : bounded;

        return Math.Clamp(size, 1, 7) switch
        {
            1 or 2 => MailTextSize.Small,
            3 => MailTextSize.Normal,
            4 => MailTextSize.Large,
            5 => MailTextSize.Larger,
            _ => MailTextSize.Largest,
        };
    }

    /// <summary>Reads the step a heading level is drawn at once its words are words rather than a heading.</summary>
    /// <param name="level">The heading level, from 1 to 6.</param>
    /// <returns>The step the level maps to.</returns>
    internal static MailTextSize OfHeadingLevel(int level) => level switch
    {
        1 => MailTextSize.Largest,
        2 => MailTextSize.Larger,
        3 => MailTextSize.Large,
        _ => MailTextSize.Normal,
    };

    private static MailTextSize? FromKeyword(string value) => value.ToUpperInvariant() switch
    {
        "XX-SMALL" or "X-SMALL" or "SMALL" => MailTextSize.Small,
        "MEDIUM" => MailTextSize.Normal,
        "LARGE" => MailTextSize.Large,
        "X-LARGE" => MailTextSize.Larger,
        "XX-LARGE" or "XXX-LARGE" => MailTextSize.Largest,
        _ => null,
    };

    private static MailTextSize? FromLength(string value)
    {
        var (digits, pixelsPerUnit) = value switch
        {
            "0" => (value, 1.0),
            _ when value.EndsWith("px", StringComparison.OrdinalIgnoreCase) => (value[..^2], 1.0),
            _ when value.EndsWith("pt", StringComparison.OrdinalIgnoreCase) => (value[..^2], PixelsPerPoint),
            _ when value.EndsWith("rem", StringComparison.OrdinalIgnoreCase) => (value[..^3], RelativeBasePixels),
            _ when value.EndsWith("em", StringComparison.OrdinalIgnoreCase) => (value[..^2], RelativeBasePixels),
            _ when value.EndsWith('%') => (value[..^1], RelativeBasePixels / 100),
            _ => (string.Empty, 0.0),
        };

        if (!double.TryParse(digits.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var length)
            || !double.IsFinite(length)
            || length < 0)
        {
            return null;
        }

        return (length * pixelsPerUnit) switch
        {
            < 12.5 => MailTextSize.Small,
            < 17 => MailTextSize.Normal,
            < 21 => MailTextSize.Large,
            < 28 => MailTextSize.Larger,
            _ => MailTextSize.Largest,
        };
    }
}
