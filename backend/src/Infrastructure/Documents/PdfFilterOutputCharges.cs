// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.IO.Compression;
using System.Runtime.InteropServices;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;
using UglyToad.PdfPig.Util;

namespace MailFathom.Infrastructure.Documents;

/// <summary>Measures what each of the PDF library's stream filters is about to produce, and charges it to the page.</summary>
/// <remarks>
/// <para>
/// Every measure reads the encoded octets and allocates nothing in proportion to the output, which is the point: a
/// measure is how a page refuses a stream before the library's decoder holds what it inflates to. Each one is exact or
/// an upper bound on what the library's own decoder produces from the same octets, read from that decoder's source at
/// the pinned version, so a stream the library would turn into more than the allowance is refused, and one it would
/// abandon part-way is charged at most what it would have produced.
/// </para>
/// <para>
/// The two that must decode in order to measure — Flate and LZW — stop the moment the allowance is passed, so a bomb
/// costs reading its compressed octets up to that point rather than inflating them all.
/// </para>
/// </remarks>
internal static class PdfFilterOutputCharges
{
    private const int LzwClearTable = 256;
    private const int LzwEndOfData = 257;
    private const int LzwFirstEntry = 258;
    private const int LzwCodeSpace = 4096;
    private const int LzwNarrowestCode = 9;

    /// <summary>Charges what a Flate stream inflates to, by inflating it into a buffer that is discarded.</summary>
    /// <remarks>
    /// <para>
    /// The library inflates into memory it keeps and offers no way to stop it part-way, so the stream is inflated here
    /// first and counted, and the library inflates it a second time only once the count is known to fit. That doubles the
    /// inflation work on an ordinary document, which is a small share of a page's cost beside the layout analysis that
    /// follows it; a decoder of this adapter's own, producing what the library would and counting as it goes, would
    /// remove it, at the price of owning the predictor functions the library already implements.
    /// </para>
    /// <para>
    /// The two octets skipped are the zlib header, which the library skips the same way. A stream the inflater rejects
    /// part-way is charged what it produced until then: the library abandons such a stream and hands back its encoded
    /// octets instead, which were already held. A predictor the stream declares is charged first, as
    /// <see cref="ChargePredictorRows" /> describes.
    /// </para>
    /// </remarks>
    public static void ChargeInflation(
        Memory<byte> input,
        DictionaryToken dictionary,
        int filterIndex,
        PdfPageDecodeBudget budget)
    {
        const int zlibHeaderOctets = 2;

        ChargePredictorRows(dictionary, filterIndex, budget);

        if (input.Length <= zlibHeaderOctets)
        {
            return;
        }

        using var compressed = ReadOnlyStreamOver(input[zlibHeaderOctets..]);
        using var inflated = new DeflateStream(compressed, CompressionMode.Decompress);

        Span<byte> discarded = stackalloc byte[8192];

        try
        {
            int read;

            while ((read = inflated.Read(discarded)) > 0)
            {
                budget.Consume(read);
            }
        }
        catch (InvalidDataException)
        {
            // Charged up to the point the data stopped being deflate, which is all the library would have produced.
        }
    }

    /// <summary>Charges what an LZW stream decodes to, by replaying the decoder's string table with lengths alone.</summary>
    /// <remarks>
    /// <para>
    /// LZW is the one filter whose decoder can be made to hold far more than it writes: the library's string table keeps
    /// every entry it ever adds, each one a copy of the previous output plus one octet, and it keeps adding them once
    /// the twelve-bit codes can no longer reach them. So each code is charged for what it writes and for the entry it
    /// adds, which is what the decoder holds after it. The replay follows the library's decoder at the pinned version —
    /// its code widths, its early change, its clear and end codes — and stops where that decoder would stop or fail. A
    /// predictor the stream declares is charged first, as <see cref="ChargePredictorRows" /> describes.
    /// </para>
    /// </remarks>
    public static void ChargeLzw(
        Memory<byte> input,
        DictionaryToken dictionary,
        int filterIndex,
        PdfPageDecodeBudget budget)
    {
        ChargePredictorRows(dictionary, filterIndex, budget);

        var parameters = DecodeParameterResolver.GetFilterParameters(dictionary, filterIndex);
        var lateChange = parameters.GetIntOrDefault(NameToken.EarlyChange, 1) == 1 ? 0 : 1;

        var data = input.Span;
        var entryLengths = new int[LzwCodeSpace];
        entryLengths.AsSpan(0, LzwClearTable).Fill(1);

        var entryCount = LzwFirstEntry;
        var codeWidth = LzwNarrowestCode;
        var previous = -1;
        var bitPosition = 0L;

        while (TryReadCode(data, ref bitPosition, codeWidth, out var code) && code != LzwEndOfData)
        {
            if (code == LzwClearTable)
            {
                entryCount = LzwFirstEntry;
                codeWidth = LzwNarrowestCode;
                previous = -1;
                continue;
            }

            var known = code < entryCount;

            // The library reads the previous code's entry here, and an entry that does not exist is where it fails.
            if (previous >= entryCount || (!known && previous < 0))
            {
                return;
            }

            var added = previous < 0 ? 0 : entryLengths[previous] + 1;
            var written = known ? entryLengths[code] : added;

            if (added > 0)
            {
                if (entryCount < LzwCodeSpace)
                {
                    entryLengths[entryCount] = added;
                }

                entryCount++;
            }

            budget.Consume((long)written + added);

            previous = code;
            codeWidth = LzwCodeWidth(entryCount, lateChange);
        }
    }

    /// <summary>Charges what a run-length stream decodes to, which its length octets state exactly.</summary>
    public static void ChargeRunLength(Memory<byte> input, PdfPageDecodeBudget budget)
    {
        const byte endOfData = 128;

        var data = input.Span;
        var decoded = 0L;
        var position = 0;

        while (position < data.Length && data[position] != endOfData)
        {
            var length = data[position];

            if (length < endOfData)
            {
                decoded += length + 1;
                position += length + 2;
            }
            else
            {
                decoded += 257 - length;
                position += 2;
            }
        }

        budget.Consume(decoded);
    }

    /// <summary>Charges an upper bound on what an ASCII base-85 stream decodes to.</summary>
    /// <remarks>
    /// Five characters decode to four octets, except <c>z</c>, which alone stands for four zero octets — so the encoded
    /// length plus three octets for every <c>z</c> is never less than what decodes.
    /// </remarks>
    public static void ChargeAscii85(Memory<byte> input, PdfPageDecodeBudget budget) =>
        budget.Consume(input.Length + (3L * input.Span.Count((byte)'z')));

    /// <summary>Charges an upper bound on what a hexadecimal stream decodes to: one octet for every two characters.</summary>
    public static void ChargeAsciiHex(Memory<byte> input, PdfPageDecodeBudget budget) =>
        budget.Consume((input.Length + 1L) / 2);

    /// <summary>Charges the bitmap a fax-encoded stream decodes into, whose size the stream's own parameters declare.</summary>
    /// <remarks>
    /// <para>
    /// The library allocates from the declared columns and rows before it reads a single code — a row buffer and two
    /// change lists of a column each, then the whole bitmap — so the declaration is the cost and is charged as it
    /// stands. Rows are chosen as the library chooses them: the image's height where both it and the parameters name a
    /// positive one, and otherwise the larger of the two.
    /// </para>
    /// <para>
    /// The library sizes the bitmap in thirty-two-bit arithmetic, so a declaration whose product is negative can wrap to
    /// a positive allocation; the larger of that wrapped figure and the true one is charged, which is never less than
    /// what it allocates.
    /// </para>
    /// </remarks>
    public static void ChargeCcittFax(
        DictionaryToken dictionary,
        int filterIndex,
        PdfPageDecodeBudget budget)
    {
        const int defaultColumns = 1728;
        const int changeListsHeld = 2;

        var parameters = DecodeParameterResolver.GetFilterParameters(dictionary, filterIndex);
        var columns = parameters.GetIntOrDefault(NameToken.Columns, defaultColumns);
        var declaredRows = parameters.GetIntOrDefault(NameToken.Rows, 0);
        var height = dictionary.GetIntOrDefault(NameToken.Height, NameToken.H, 0);
        var rows = declaredRows > 0 && height > 0 ? height : Math.Max(declaredRows, height);

        var rowOctets = unchecked((columns + 7) / 8);
        var workingOctets = rowOctets + (changeListsHeld * sizeof(int) * ((long)columns + 2));
        var bitmapOctets = Math.Max(unchecked(rowOctets * rows), (long)rowOctets * rows);

        budget.Consume(Math.Max(0L, workingOctets) + Math.Max(0L, bitmapOctets));
    }

    /// <summary>Charges the rows a predictor declared on a Flate or LZW stream holds, before anything is decoded.</summary>
    /// <remarks>
    /// <para>
    /// A predictor reverses a per-row encoding after the stream decodes, and the library allocates its two row buffers
    /// from the row length the stream's parameters declare — columns times colours times bits per component — before
    /// it reads a single octet, then writes whole rows, padding the last. So a few hundred compressed octets declaring
    /// thirty million columns of sixteen-bit colour hold gigabytes that no measure of the decoded octets sees. The
    /// declaration is the cost, as it is for a fax bitmap, and three rows are charged for it: the two buffers and the
    /// padded row the output can end on. The decoded octets themselves are charged by the filter's own measure.
    /// </para>
    /// <para>
    /// The row length is computed the way the library computes it, with its cap of thirty-two colours. Its arithmetic is
    /// thirty-two-bit, so a declaration past that range wraps there — a positive one into some smaller allocation, and
    /// a negative one possibly into a large positive allocation. Both the wrapped figure and the true one are computed,
    /// and the larger is charged, which is never less than what the library allocates.
    /// </para>
    /// </remarks>
    private static void ChargePredictorRows(DictionaryToken dictionary, int filterIndex, PdfPageDecodeBudget budget)
    {
        const int maxColors = 32;
        const int rowsHeld = 3;

        var parameters = DecodeParameterResolver.GetFilterParameters(dictionary, filterIndex);

        if (parameters.GetIntOrDefault(NameToken.Predictor, -1) <= 1)
        {
            return;
        }

        var colors = Math.Min(parameters.GetIntOrDefault(NameToken.Colors, 1), maxColors);
        var bitsPerComponent = parameters.GetIntOrDefault(NameToken.BitsPerComponent, 8);
        var columns = parameters.GetIntOrDefault(NameToken.Columns, 1);
        var libraryRowLength = unchecked(((columns * (colors * bitsPerComponent)) + 7) / 8);
        var rowLength = Int128.Max(libraryRowLength, (((Int128)columns * colors * bitsPerComponent) + 7) / 8);

        budget.Consume(rowLength <= 0 ? 0 : (long)Int128.Min(rowLength * rowsHeld, long.MaxValue));
    }

    private static MemoryStream ReadOnlyStreamOver(Memory<byte> octets) =>
        MemoryMarshal.TryGetArray<byte>(octets, out var segment)
            ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(octets.ToArray(), writable: false);

    private static bool TryReadCode(ReadOnlySpan<byte> data, ref long bitPosition, int width, out int code)
    {
        code = 0;

        if (bitPosition + width > data.Length * 8L)
        {
            return false;
        }

        for (var bit = 0; bit < width; bit++, bitPosition++)
        {
            var octet = data[(int)(bitPosition >> 3)];

            code = (code << 1) | ((octet >> (7 - (int)(bitPosition & 7))) & 1);
        }

        return true;
    }

    private static int LzwCodeWidth(int entryCount, int lateChange) =>
        entryCount >= 2047 + lateChange ? 12
        : entryCount >= 1023 + lateChange ? 11
        : entryCount >= 511 + lateChange ? 10
        : LzwNarrowestCode;
}
