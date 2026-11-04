// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Emails.Extraction.Attachments;

/// <summary>Bounds what reading one attachment's text may consume, and which formats are offered to a parser at all.</summary>
/// <remarks>
/// <para>
/// Every value here is a ceiling rather than a target, and each one exists because a sender chose the bytes. A document
/// parser is the largest attack surface this system has: an archive with a small compressed size and an enormous
/// declared one, an element tree nested until a walk runs out of stack, and a page that decodes into more text than a
/// mailbox holds in a year are all ordinary shapes of a mail-borne attack rather than corner cases. Every value on it
/// is set once and read-only afterwards, because the instance is registered as a singleton: a mutable one would let
/// anything resolving it raise a ceiling at run time past the range a start validated, or widen the one value deciding
/// which sender-composed formats reach a parser at all.
/// </para>
/// <para>
/// Exceeding any of them abandons the attachment and says which ceiling stopped it. Nothing is truncated into a partial
/// answer, because a partial extract presented as an extract is indistinguishable from a document that really said
/// only that much.
/// </para>
/// </remarks>
public sealed class AttachmentTextExtractionOptions
{
    /// <summary>The octets one attachment may hold where a deployment declares no ceiling of its own.</summary>
    public const long DefaultMaxInputOctets = 16L * 1024 * 1024;

    /// <summary>The characters one attachment may contribute where a deployment declares no ceiling of its own.</summary>
    public const int DefaultMaxExtractedTextCharacters = 200_000;

    /// <summary>The octets a container may inflate to where a deployment declares no ceiling of its own.</summary>
    public const long DefaultMaxDecompressedOctets = 64L * 1024 * 1024;

    /// <summary>The inflation ratio one container part may reach where a deployment declares no ceiling of its own.</summary>
    public const int DefaultMaxDecompressionRatio = 200;

    /// <summary>The octets one PDF page's streams may decode to where a deployment declares no ceiling of its own.</summary>
    public const long DefaultMaxPdfPageDecodedOctets = 64L * 1024 * 1024;

    /// <summary>The parts a container may declare where a deployment declares no ceiling of its own.</summary>
    public const int DefaultMaxContainerParts = 2_000;

    /// <summary>The depth an element tree may nest to where a deployment declares no ceiling of its own.</summary>
    public const int DefaultMaxElementDepth = 100;

    /// <summary>The time one extraction may take where a deployment declares no ceiling of its own.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Gets the formats an attachment is offered to a parser for.</summary>
    /// <remarks>
    /// The undertaking rather than whatever arrives: an attachment whose recognized format is absent from this list is
    /// skipped without a parser ever seeing its bytes, which is what lets a deployment narrow the surface it accepts
    /// without narrowing what MailFathom is able to read. It defaults to every format a parser here reads.
    /// </remarks>
    public IReadOnlyList<AttachmentDocumentFormat> Formats { get; init; } = AttachmentDocumentFormats.Extracted;

    /// <summary>Gets the greatest number of octets one attachment may hold before it is read at all.</summary>
    /// <remarks>
    /// The bound on what is buffered. Every parser here needs to seek, so the content is held in memory for the length
    /// of one extraction, and this is what stops a single attachment from deciding how much memory the process needs.
    /// Sixteen mebibytes is well above the size a mail server accepts an attachment at.
    /// </remarks>
    public long MaxInputOctets { get; init; } = DefaultMaxInputOctets;

    /// <summary>Gets the greatest number of characters one attachment may contribute.</summary>
    /// <remarks>
    /// A ceiling on the output rather than on the input, because the two are not proportional: a compressed page of a
    /// document expands into text at a ratio the sender chooses. The default is the same order as the per-message
    /// ceiling extracted mail text is held to.
    /// </remarks>
    public int MaxExtractedTextCharacters { get; init; } = DefaultMaxExtractedTextCharacters;

    /// <summary>Gets the total octets an archive format may decompress to.</summary>
    /// <remarks>
    /// Counted incrementally across every part of an Office Open XML or OpenDocument archive read, so an archive declaring an enormous uncompressed size is abandoned
    /// while it inflates rather than after it has finished. The declared size is never read: it is the sender's
    /// number, and a bomb is precisely a file that lies about it.
    /// </remarks>
    public long MaxDecompressedOctets { get; init; } = DefaultMaxDecompressedOctets;

    /// <summary>Gets the greatest ratio of decompressed to compressed octets one archive part may reach.</summary>
    /// <remarks>
    /// The second half of the same guard, and the half that catches a small archive: a part whose inflation runs far
    /// past what its compressed length can honestly explain is abandoned before <see cref="MaxDecompressedOctets" />
    /// would have been reached. Ordinary office markup compresses somewhere below twenty to one. A PDF's streams are not
    /// held to it: they include predictor-coded tables and fax-coded bitmaps, which compress far past what markup does,
    /// so a ratio there would refuse ordinary documents while <see cref="MaxPdfPageDecodedOctets" /> already bounds what a
    /// page may hold.
    /// </remarks>
    public int MaxDecompressionRatio { get; init; } = DefaultMaxDecompressionRatio;

    /// <summary>Gets the octets a PDF's streams may decode to while one page is read.</summary>
    /// <remarks>
    /// <para>
    /// Counted as each stream is about to decode rather than after, across every stream the page reaches — its content,
    /// the forms it draws, the fonts and character maps it loads — with what the document decodes while it is opened
    /// counting towards its first page. A page past it abandons the whole document, so a content stream that is a
    /// decompression bomb is refused while its compressed octets are read rather than once it has inflated.
    /// </para>
    /// <para>
    /// Per page rather than per document because the library decodes a form each time a page draws it: a letterhead
    /// repeated on every page of a long report is decoded once per page, and a document total would refuse that report
    /// for being long. Sixty-four mebibytes is four times the default input ceiling and far past what one honest page
    /// holds once decoded — a page's content stream is rarely more than a few mebibytes, and the largest single stream an
    /// ordinary document carries, a fully embedded font for a large script, is well inside it. It is also what bounds the
    /// work inside one page, which <see cref="Timeout" /> cannot interrupt.
    /// </para>
    /// </remarks>
    public long MaxPdfPageDecodedOctets { get; init; } = DefaultMaxPdfPageDecodedOctets;

    /// <summary>Gets the greatest number of parts a container format may hold.</summary>
    /// <remarks>
    /// An archive of very many tiny parts costs per part rather than per octet, which neither size bound above
    /// measures. It is checked against what the archive's own directory declares, and reading that directory is what
    /// answers the count — so this bounds the parts a reader will go on to open rather than the cost of learning how
    /// many there are. That cost is bounded instead by <see cref="MaxInputOctets" />, since a directory entry cannot
    /// be smaller than its own record and the whole archive was already refused above this ceiling.
    /// </remarks>
    public int MaxContainerParts { get; init; } = DefaultMaxContainerParts;

    /// <summary>Gets the greatest depth an element tree inside a container part may nest to.</summary>
    /// <remarks>Deep nesting is what turns a small part into a walk that consumes stack rather than time.</remarks>
    public int MaxElementDepth { get; init; } = DefaultMaxElementDepth;

    /// <summary>Gets the time one extraction may take before it is abandoned.</summary>
    /// <remarks>
    /// Observed between units of work — a PDF page, an archive part, an element — because no parser here accepts a
    /// cancellation token of its own. A parser that never returns from one unit is therefore bounded by whatever else
    /// bounds its path rather than by this: for an archive that is the decompression, ratio, and depth ceilings above,
    /// and for a PDF it is <see cref="MaxPdfPageDecodedOctets" />, which is why none of those is optional.
    /// </remarks>
    public TimeSpan Timeout { get; init; } = DefaultTimeout;

    /// <summary>Gets how many of the pictures inside one document are copied out for a model to read.</summary>
    /// <remarks>
    /// Zero, which reads none, unless the deployment turned image description on — the pictures in a document are
    /// sent exactly where an image attachment would be. Every picture copied out is one chat call, and the aggregate
    /// description ceiling is checked before a message rather than inside one, so this is what bounds how far one
    /// message may run past that ceiling. A picture too small to carry a page and a copy of one already taken are not
    /// counted, so a letterhead repeated on every page cannot use up the allowance before the scanned page is reached.
    /// </remarks>
    public int MaxPicturesPerDocument { get; init; }
}
