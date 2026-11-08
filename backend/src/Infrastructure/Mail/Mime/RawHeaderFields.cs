// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Buffers;
using System.Text;

namespace MailFathom.Infrastructure.Mail.Mime;

/// <summary>Rewrites one field of a message's top-level header block in place, leaving every other byte as it was.</summary>
/// <remarks>
/// It works on the bytes rather than on a parsed message, because parsing and writing a message back refolds headers
/// and may re-encode parts, and a message the outbox stored is what every attempt and every sent copy derives from: the
/// one field asked for changes and nothing else does. A field is a line beginning with its name and a colon together
/// with the folded lines after it, which begin with a space or a tab; the block ends at the first empty line.
/// </remarks>
internal static class RawHeaderFields
{
    /// <summary>Replaces every instance of one field with a single new field, or removes them all.</summary>
    /// <param name="rawMime">The message.</param>
    /// <param name="name">The field's name, matched without regard to ASCII case.</param>
    /// <param name="value">The new field's value, or <see langword="null" /> to remove the field.</param>
    /// <returns>The message with the field rewritten: the new field takes the place of the first instance, or opens the block where there was none.</returns>
    internal static byte[] Replace(ReadOnlySpan<byte> rawMime, string name, string? value)
    {
        var newLine = rawMime.IndexOf((byte)'\n') is > 0 and var firstEnd && rawMime[firstEnd - 1] == (byte)'\r'
            ? "\r\n"
            : "\n";
        var replacement = value is null ? [] : Encoding.ASCII.GetBytes($"{name}: {value}{newLine}");

        var output = new ArrayBufferWriter<byte>(rawMime.Length + replacement.Length);
        var replaced = value is null;
        var position = 0;

        while (position < rawMime.Length && !IsBlankLine(rawMime[position..EndOfLine(rawMime, position)]))
        {
            var fieldEnd = EndOfField(rawMime, position);

            if (!NamesField(rawMime[position..], name))
            {
                output.Write(rawMime[position..fieldEnd]);
            }
            else if (!replaced)
            {
                output.Write(replacement);
                replaced = true;
            }

            position = fieldEnd;
        }

        output.Write(rawMime[position..]);

        return replaced ? output.WrittenSpan.ToArray() : [.. replacement, .. output.WrittenSpan];
    }

    private static int EndOfLine(ReadOnlySpan<byte> rawMime, int start) =>
        rawMime[start..].IndexOf((byte)'\n') is >= 0 and var offset ? start + offset + 1 : rawMime.Length;

    private static int EndOfField(ReadOnlySpan<byte> rawMime, int start)
    {
        var end = EndOfLine(rawMime, start);

        while (end < rawMime.Length && rawMime[end] is (byte)' ' or (byte)'\t')
        {
            end = EndOfLine(rawMime, end);
        }

        return end;
    }

    private static bool IsBlankLine(ReadOnlySpan<byte> line) => line.TrimEnd("\r\n"u8).IsEmpty;

    /// <summary>Reports whether a field begins with a name, allowing the white space before the colon the obsolete syntax permits.</summary>
    private static bool NamesField(ReadOnlySpan<byte> field, string name)
    {
        if (field.Length <= name.Length || !Ascii.EqualsIgnoreCase(field[..name.Length], name))
        {
            return false;
        }

        return field[name.Length..].TrimStart(" \t"u8) is [(byte)':', ..];
    }
}
