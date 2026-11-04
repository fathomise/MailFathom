// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;

namespace MailFathom.Infrastructure.Documents;

/// <summary>Hands the PDF library its own stream filters, each charging the page's allowance before it decodes.</summary>
/// <remarks>
/// <para>
/// Every stream the library decodes — the cross-reference and object streams it opens a document with, a page's content
/// streams, the forms a page draws, fonts, character maps — goes through the one filter provider its parsing options
/// name, which is what makes this the seam: the decoders stay the library's, and each is preceded by a charge that
/// measures what it is about to produce without producing it. A charge past the allowance throws before the decoder
/// allocates anything, so a bomb is refused at the cost of reading its compressed octets rather than of holding what
/// they inflate to.
/// </para>
/// <para>
/// The three image filters are passed through uncharged because the library refuses to decode any of them, and nothing
/// on this path asks it to: a page's pictures are taken as the document stores them. A test pins that refusal, so a
/// library release that starts decoding one fails there rather than opening a decoder no charge stands in front of.
/// </para>
/// </remarks>
internal sealed class BoundedPdfFilterProvider(PdfPageDecodeBudget budget) : BaseFilterProvider(ChargedFilters(budget))
{
    private static Dictionary<string, IFilter> ChargedFilters(PdfPageDecodeBudget budget)
    {
        var flate = new ChargedFilter(
            new FlateFilter(),
            (input, dictionary, filterIndex) => PdfFilterOutputCharges.ChargeInflation(input, dictionary, filterIndex, budget));
        var lzw = new ChargedFilter(
            new LzwFilter(),
            (input, dictionary, filterIndex) => PdfFilterOutputCharges.ChargeLzw(input, dictionary, filterIndex, budget));
        var runLength = new ChargedFilter(
            new RunLengthFilter(),
            (input, _, _) => PdfFilterOutputCharges.ChargeRunLength(input, budget));
        var ascii85 = new ChargedFilter(
            new Ascii85Filter(),
            (input, _, _) => PdfFilterOutputCharges.ChargeAscii85(input, budget));
        var asciiHex = new ChargedFilter(
            new AsciiHexDecodeFilter(),
            (input, _, _) => PdfFilterOutputCharges.ChargeAsciiHex(input, budget));
        var ccittFax = new ChargedFilter(
            new CcittFaxDecodeFilter(),
            (_, dictionary, filterIndex) => PdfFilterOutputCharges.ChargeCcittFax(dictionary, filterIndex, budget));
        var dct = new DctDecodeFilter();

        return new Dictionary<string, IFilter>(StringComparer.Ordinal)
        {
            [NameToken.FlateDecode.Data] = flate,
            [NameToken.FlateDecodeAbbreviation.Data] = flate,
            [NameToken.LzwDecode.Data] = lzw,
            [NameToken.LzwDecodeAbbreviation.Data] = lzw,
            [NameToken.RunLengthDecode.Data] = runLength,
            [NameToken.RunLengthDecodeAbbreviation.Data] = runLength,
            [NameToken.Ascii85Decode.Data] = ascii85,
            [NameToken.Ascii85DecodeAbbreviation.Data] = ascii85,
            [NameToken.AsciiHexDecode.Data] = asciiHex,
            [NameToken.AsciiHexDecodeAbbreviation.Data] = asciiHex,
            [NameToken.CcittfaxDecode.Data] = ccittFax,
            [NameToken.CcittfaxDecodeAbbreviation.Data] = ccittFax,
            [NameToken.DctDecode.Data] = dct,
            [NameToken.DctDecodeAbbreviation.Data] = dct,
            [NameToken.JpxDecode.Data] = new JpxDecodeFilter(),
            [NameToken.Jbig2Decode.Data] = new Jbig2DecodeFilter(),
        };
    }

    /// <summary>One of the library's filters, preceded by the charge for what it is about to produce.</summary>
    private sealed class ChargedFilter(IFilter decoder, Action<Memory<byte>, DictionaryToken, int> charge) : IFilter
    {
        /// <inheritdoc />
        public bool IsSupported => decoder.IsSupported;

        /// <inheritdoc />
        public Memory<byte> Decode(
            Memory<byte> input,
            DictionaryToken streamDictionary,
            IFilterProvider filterProvider,
            int filterIndex)
        {
            charge(input, streamDictionary, filterIndex);

            return decoder.Decode(input, streamDictionary, filterProvider, filterIndex);
        }
    }
}
