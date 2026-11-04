// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Collections.Frozen;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using MailFathom.Application.Emails.Extraction.Attachments;

namespace MailFathom.Infrastructure.Documents;

/// <summary>Reads the text of an Office Open XML document out of the archive it is packaged as.</summary>
/// <remarks>
/// <para>
/// The three Office Open XML formats are zip archives of XML parts, and the base class library reads both halves — so
/// this walks them directly rather than through a document model. That is not only the smaller dependency: a document
/// model inflates a part before handing it over, which is precisely the moment a decompression bomb has already won.
/// Reading the archive here is what makes <see cref="BoundedInflationStream" /> possible at all.
/// </para>
/// <para>
/// Only the parts that carry text are opened — for a word-processing document that is the body, its headers, its
/// footers, and its two note parts; for a deck and a workbook, the numbered page parts, the string table, and the part
/// declaring the pages' order with the relationship part it resolves through. A macro
/// project, an embedded object, an OLE package, and every other part of the package are never read, never decoded,
/// and never handed to anything — extraction reads structure and text and evaluates nothing.
/// </para>
/// <para>
/// The one exception is a picture, and only where the deployment reads pictures: the images a word-processing document
/// stores under <c>word/media/</c>, and the images each slide's relationships name, are copied out as they are stored
/// for a model to read. A pasted scan of an invoice is words, and a slide's photograph is what the slide shows. A
/// workbook's pictures are not read. Nothing here decodes a picture: it is inflated out of the archive under the same
/// budget as every text part and handed on as the octets the package holds.
/// </para>
/// </remarks>
internal sealed partial class OpenXmlAttachmentTextReader(AttachmentTextExtractionOptions options)
{
    /// <summary>The namespaces a word-processing part writes its text in, one per conformance class.</summary>
    /// <remarks>
    /// ISO 29500 defines two conformance classes, and a package written under the Strict one declares every part in a
    /// namespace of its own. Matching only the Transitional namespace would walk a Strict document correctly, recognize
    /// nothing in it, and report the empty result as a page carrying no text layer — which is how this reader reports a
    /// scan, so a caller acting on <c>PagesWithoutText</c> would be told a specific wrong thing rather than nothing.
    /// The admission is a set per format so that a further conformance class is one entry rather than an edit at each
    /// comparison site.
    /// </remarks>
    private static readonly FrozenSet<string> WordprocessingNamespaces = FrozenSet.Create(
        StringComparer.Ordinal,
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main",
        "http://purl.oclc.org/ooxml/wordprocessingml/main");

    /// <summary>The namespaces a workbook's parts are written in, one per conformance class.</summary>
    private static readonly FrozenSet<string> SpreadsheetNamespaces = FrozenSet.Create(
        StringComparer.Ordinal,
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main",
        "http://purl.oclc.org/ooxml/spreadsheetml/main");

    /// <summary>The namespaces a slide writes its text in, one per conformance class.</summary>
    private static readonly FrozenSet<string> DrawingNamespaces = FrozenSet.Create(
        StringComparer.Ordinal,
        "http://schemas.openxmlformats.org/drawingml/2006/main",
        "http://purl.oclc.org/ooxml/drawingml/main");

    /// <summary>The namespaces a presentation's own part declares its slide list in, one per conformance class.</summary>
    private static readonly FrozenSet<string> PresentationNamespaces = FrozenSet.Create(
        StringComparer.Ordinal,
        "http://schemas.openxmlformats.org/presentationml/2006/main",
        "http://purl.oclc.org/ooxml/presentationml/main");

    /// <summary>The namespaces a part names a relationship identifier in, one per conformance class.</summary>
    private static readonly FrozenSet<string> RelationshipNamespaces = FrozenSet.Create(
        StringComparer.Ordinal,
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships",
        "http://purl.oclc.org/ooxml/officeDocument/relationships");

    private const string WordDocumentPart = "word/document.xml";
    private const string PresentationPart = "ppt/presentation.xml";
    private const string WorkbookPart = "xl/workbook.xml";
    private const string SharedStringsPart = "xl/sharedStrings.xml";

    /// <summary>The parts a word-processing document keeps its notes in, read after the body and in this order.</summary>
    private static readonly string[] WordNoteParts = ["word/footnotes.xml", "word/endnotes.xml"];

    private readonly BoundedArchivePartReader parts = new(options);

    /// <summary>Reads one Office Open XML attachment.</summary>
    /// <param name="content">The attachment's octets, positioned at the start.</param>
    /// <param name="format">Which of the three Office Open XML formats it is.</param>
    /// <param name="cancellationToken">Cancels the read between parts and between elements.</param>
    /// <returns>What the document yielded.</returns>
    /// <exception cref="AttachmentTextExtractionStoppedException">Thrown when a configured ceiling is crossed.</exception>
    /// <exception cref="InvalidDataException">Thrown when the octets are not a readable archive, or the package declares none of the parts its format is read from.</exception>
    /// <exception cref="XmlException">Thrown when a part is not readable XML.</exception>
    public ExtractedAttachmentText Read(
        Stream content,
        AttachmentDocumentFormat format,
        CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);

        if (archive.Entries.Count > options.MaxContainerParts)
        {
            throw new AttachmentTextExtractionStoppedException(AttachmentTextExtractionOutcome.ContainerBoundExceeded);
        }

        var budget = new DecompressionBudget(options.MaxDecompressedOctets, content.Length);
        var text = new BoundedTextAccumulator(options.MaxExtractedTextCharacters);
        var pictures = new EmbeddedPictureCollector(options.MaxPicturesPerDocument);

        var extracted = format switch
        {
            AttachmentDocumentFormat.WordOpenXml => this.ReadDocument(archive, budget, text, pictures, cancellationToken),
            AttachmentDocumentFormat.PresentationOpenXml => this.ReadPresentation(archive, budget, text, pictures, cancellationToken),
            AttachmentDocumentFormat.SpreadsheetOpenXml => this.ReadWorkbook(archive, budget, text, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "The format is not an Office Open XML package."),
        };

        return extracted with { Pictures = pictures.Pictures };
    }

    [GeneratedRegex(@"^ppt/slides/slide(\d+)\.xml$", RegexOptions.IgnoreCase)]
    private static partial Regex SlidePartPattern();

    [GeneratedRegex(@"^word/header(\d+)\.xml$", RegexOptions.IgnoreCase)]
    private static partial Regex HeaderPartPattern();

    [GeneratedRegex(@"^word/footer(\d+)\.xml$", RegexOptions.IgnoreCase)]
    private static partial Regex FooterPartPattern();

    [GeneratedRegex(@"^xl/worksheets/sheet(\d+)\.xml$", RegexOptions.IgnoreCase)]
    private static partial Regex WorksheetPartPattern();

    [GeneratedRegex(@"^word/media/[^/]+$", RegexOptions.IgnoreCase)]
    private static partial Regex WordMediaPartPattern();

    /// <summary>Reads a word-processing document, whose body is one page because the format records no pagination.</summary>
    /// <remarks>
    /// The body is not the whole of what somebody wrote. A letterhead's invoice number lives in a header part, a
    /// contract's terms often in footnotes, and each of those is a part of its own — so they are read after the body
    /// and in a fixed order, which is the honest arrangement available when the format records where a header prints
    /// rather than where its words belong in a reading. Everything else in the package stays unopened.
    /// </remarks>
    private ExtractedAttachmentText ReadDocument(
        ZipArchive archive,
        DecompressionBudget budget,
        BoundedTextAccumulator text,
        EmbeddedPictureCollector pictures,
        CancellationToken cancellationToken)
    {
        var document = archive.GetEntry(WordDocumentPart)
            ?? throw new InvalidDataException("The package declares no word-processing document part.");

        bool carriedText;

        using (var reader = this.parts.OpenPart(document, budget))
        {
            carriedText = this.ReadRunsInto(reader, WordprocessingNamespaces, text, cancellationToken);
        }

        foreach (var part in SurroundingWordParts(archive))
        {
            text.EndLine();

            using var reader = this.parts.OpenPart(part, budget);

            carriedText |= this.ReadRunsInto(reader, WordprocessingNamespaces, text, cancellationToken);
        }

        var media = archive.Entries
            .Where(entry => WordMediaPartPattern().IsMatch(entry.FullName))
            .OrderBy(entry => entry.FullName, StringComparer.Ordinal);

        this.OfferPictures(media, pageNumber: 1, budget, pictures, cancellationToken);

        return new ExtractedAttachmentText(
            text.ToText(),
            PageCount: 1,
            carriedText ? [] : [1],
            [new AttachmentTextSegment(AttachmentTextSegmentKind.Page, Number: 1, Label: null, StartOffset: 0)]);
    }

    /// <summary>Selects the header, footer, and note parts of a word-processing package, in the order they are read.</summary>
    private static List<ZipArchiveEntry> SurroundingWordParts(ZipArchive archive) =>
    [
        .. OrderedParts(archive, HeaderPartPattern()),
        .. OrderedParts(archive, FooterPartPattern()),
        .. WordNoteParts.Select(archive.GetEntry).OfType<ZipArchiveEntry>(),
    ];

    /// <summary>Reads a presentation, one page per slide in the order the deck presents them.</summary>
    /// <remarks>
    /// That order is the deck's <c>sldIdLst</c>, not the slide parts' names: reordering or deleting a slide leaves the
    /// part names where they were, so reading by name would number every page a person counts to differently.
    /// </remarks>
    private ExtractedAttachmentText ReadPresentation(
        ZipArchive archive,
        DecompressionBudget budget,
        BoundedTextAccumulator text,
        EmbeddedPictureCollector pictures,
        CancellationToken cancellationToken)
    {
        var slides = this.InDeclaredOrder(
            archive,
            OrderedParts(archive, SlidePartPattern()),
            PresentationPart,
            "sldId",
            PresentationNamespaces,
            budget,
            cancellationToken);

        if (slides.Count == 0)
        {
            throw new InvalidDataException("The package declares no slide part.");
        }

        var slidesWithoutText = new List<int>();
        var segments = new List<AttachmentTextSegment>();

        foreach (var (slide, index) in slides.Select((slide, index) => (slide, index)))
        {
            segments.Add(new AttachmentTextSegment(
                AttachmentTextSegmentKind.Slide,
                index + 1,
                Label: null,
                text.Length));

            bool carriedText;

            using (var reader = this.parts.OpenPart(slide, budget))
            {
                carriedText = this.ReadRunsInto(reader, DrawingNamespaces, text, cancellationToken);
            }

            if (!carriedText)
            {
                slidesWithoutText.Add(index + 1);
            }

            text.EndLine();

            if (pictures.WantsMore)
            {
                this.OfferPictures(
                    this.SlidePictureParts(archive, slide, budget, cancellationToken),
                    index + 1,
                    budget,
                    pictures,
                    cancellationToken);
            }
        }

        return new ExtractedAttachmentText(text.ToText(), slides.Count, slidesWithoutText, segments);
    }

    /// <summary>Reads a workbook, one page per worksheet in the order its tabs are in, resolving the table each cell indexes into.</summary>
    /// <remarks>
    /// <para>
    /// That order is the <c>sheets</c> element of <c>xl/workbook.xml</c>, not the worksheet parts' names, which stay
    /// where they were when a tab is moved or deleted.
    /// </para>
    /// <para>
    /// The string table is resolved rather than emitted whole. Emitting it would produce every word in the workbook
    /// attached to no sheet, which reads as text and says nothing about where the text is — and a sheet holding only a
    /// picture would be indistinguishable from one whose words are in the table.
    /// </para>
    /// </remarks>
    private ExtractedAttachmentText ReadWorkbook(
        ZipArchive archive,
        DecompressionBudget budget,
        BoundedTextAccumulator text,
        CancellationToken cancellationToken)
    {
        var sharedStrings = this.ReadSharedStrings(archive, budget, cancellationToken);
        var sheets = this.InDeclaredOrder(
            archive,
            OrderedParts(archive, WorksheetPartPattern()),
            WorkbookPart,
            "sheet",
            SpreadsheetNamespaces,
            budget,
            cancellationToken);

        if (sheets.Count == 0)
        {
            throw new InvalidDataException("The package declares no worksheet part.");
        }

        var sheetsWithoutText = new List<int>();
        var segments = new List<AttachmentTextSegment>();

        foreach (var (sheet, index) in sheets.Select((sheet, index) => (sheet, index)))
        {
            // The sheet is as far inside a workbook as a citation reaches here. A cell range would need a boundary per
            // row, which for a real workbook is tens of thousands of segments hanging off one attachment, and the
            // sheet is what sends a reader to the right place at a cost proportional to the sheets rather than to the
            // rows. The name the workbook records for the sheet is not recorded: it is text a sender chose, and carrying
            // it into every citation is issue #2990.
            segments.Add(new AttachmentTextSegment(
                AttachmentTextSegmentKind.Sheet,
                index + 1,
                Label: null,
                text.Length));

            bool carriedText;

            using (var reader = this.parts.OpenPart(sheet, budget))
            {
                carriedText = this.ReadCellsInto(reader, sharedStrings, text, cancellationToken);
            }

            if (!carriedText)
            {
                sheetsWithoutText.Add(index + 1);
            }

            text.EndLine();
        }

        return new ExtractedAttachmentText(text.ToText(), sheets.Count, sheetsWithoutText, segments);
    }

    /// <summary>Copies each picture part out of the archive and offers it to the collector, while it still wants one.</summary>
    private void OfferPictures(
        IEnumerable<ZipArchiveEntry> parts,
        int pageNumber,
        DecompressionBudget budget,
        EmbeddedPictureCollector pictures,
        CancellationToken cancellationToken)
    {
        foreach (var part in parts)
        {
            if (!pictures.WantsMore)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();

            pictures.Offer(pageNumber, this.parts.ReadPartOctets(part, budget));
        }
    }

    /// <summary>Resolves the picture parts one slide's relationships name, in the order they are declared.</summary>
    /// <remarks>
    /// A target is a path relative to the slide's own folder, written by whoever composed the package, so it is
    /// resolved against the archive's own entries and anything it does not name there — an external link, a path
    /// climbing out of the package — resolves to nothing.
    /// </remarks>
    private List<ZipArchiveEntry> SlidePictureParts(
        ZipArchive archive,
        ZipArchiveEntry slide,
        DecompressionBudget budget,
        CancellationToken cancellationToken)
    {
        var folder = slide.FullName[..(slide.FullName.LastIndexOf('/') + 1)];
        var relationships = archive.GetEntry($"{folder}_rels/{slide.Name}.rels");
        var found = new List<ZipArchiveEntry>();

        if (relationships is null)
        {
            return found;
        }

        using var reader = this.parts.OpenPart(relationships, budget);

        while (this.parts.ReadNode(reader, cancellationToken))
        {
            if (reader.NodeType != XmlNodeType.Element
                || reader.LocalName != "Relationship"
                || reader.GetAttribute("TargetMode") is "External"
                || reader.GetAttribute("Type")?.EndsWith("/image", StringComparison.Ordinal) is not true
                || reader.GetAttribute("Target") is not { } target
                || archive.GetEntry(ResolvePartName(folder, target)) is not { } picture)
            {
                continue;
            }

            found.Add(picture);
        }

        return found;
    }

    /// <summary>Puts the page parts found by name into the order the package declares for them, where it declares one.</summary>
    /// <param name="archive">The package.</param>
    /// <param name="named">The page parts, in part-name order.</param>
    /// <param name="orderingPartName">The part whose list of relationship identifiers is the declared order.</param>
    /// <param name="orderElementName">The element in that list naming one page.</param>
    /// <param name="orderNamespaces">The namespaces that element is admitted in.</param>
    /// <param name="budget">What the whole container has left to inflate to.</param>
    /// <param name="cancellationToken">Cancels the read between elements.</param>
    /// <returns>The same parts in the declared order, or in part-name order when the package declares none worth taking.</returns>
    /// <remarks>
    /// The declared order is taken only when it names every page part exactly once. A package without the ordering part
    /// or its relationship part, with either of them unreadable as XML, or whose order names a part it does not hold,
    /// names one twice, or leaves one out, is read in part-name order instead: no office suite writes such a package,
    /// and refusing the attachment over its order would lose text that is still there to search. A ceiling crossed
    /// while reading either part still stops the whole read, exactly as it does in any other part.
    /// </remarks>
    private List<ZipArchiveEntry> InDeclaredOrder(
        ZipArchive archive,
        List<ZipArchiveEntry> named,
        string orderingPartName,
        string orderElementName,
        FrozenSet<string> orderNamespaces,
        DecompressionBudget budget,
        CancellationToken cancellationToken)
    {
        var separator = orderingPartName.LastIndexOf('/') + 1;
        var folder = orderingPartName[..separator];
        var orderingPart = archive.GetEntry(orderingPartName);
        var relationshipsPart = archive.GetEntry($"{folder}_rels/{orderingPartName[separator..]}.rels");

        if (named.Count == 0 || orderingPart is null || relationshipsPart is null)
        {
            return named;
        }

        List<ZipArchiveEntry>? declared;

        try
        {
            var targets = this.PageRelationshipTargets(relationshipsPart, folder, named, budget, cancellationToken);

            declared = this.DeclaredPages(
                orderingPart,
                orderElementName,
                orderNamespaces,
                targets,
                named.Count,
                budget,
                cancellationToken);
        }
        catch (XmlException)
        {
            return named;
        }

        return declared is not null && declared.Count == named.Count && declared.Distinct().Count() == named.Count
            ? declared
            : named;
    }

    /// <summary>Reads which page part each relationship identifier of an ordering part names.</summary>
    /// <remarks>
    /// Only a relationship whose target is one of the page parts is kept, so what is held stays proportional to the
    /// pages rather than to whatever else the relationship part lists. A part name is compared without regard to case,
    /// because the packaging convention compares part names that way.
    /// </remarks>
    private Dictionary<string, ZipArchiveEntry> PageRelationshipTargets(
        ZipArchiveEntry relationshipsPart,
        string folder,
        List<ZipArchiveEntry> named,
        DecompressionBudget budget,
        CancellationToken cancellationToken)
    {
        var pagesByName = named
            .DistinctBy(page => page.FullName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(page => page.FullName, StringComparer.OrdinalIgnoreCase);
        var targets = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);

        using var reader = this.parts.OpenPart(relationshipsPart, budget);

        while (this.parts.ReadNode(reader, cancellationToken))
        {
            if (reader.NodeType == XmlNodeType.Element
                && reader.LocalName == "Relationship"
                && reader.GetAttribute("TargetMode") is not "External"
                && reader.GetAttribute("Id") is { } identifier
                && reader.GetAttribute("Target") is { } target
                && pagesByName.TryGetValue(ResolvePartName(folder, target), out var page))
            {
                targets.TryAdd(identifier, page);
            }
        }

        return targets;
    }

    /// <summary>Reads the pages an ordering part lists, in its order, resolved through its relationships.</summary>
    /// <returns>
    /// The pages listed, or <see langword="null" /> once an entry names no page part or the list grows past the pages
    /// there are — either of which already means the declared order cannot be taken.
    /// </returns>
    private List<ZipArchiveEntry>? DeclaredPages(
        ZipArchiveEntry orderingPart,
        string orderElementName,
        FrozenSet<string> orderNamespaces,
        Dictionary<string, ZipArchiveEntry> targets,
        int pageCount,
        DecompressionBudget budget,
        CancellationToken cancellationToken)
    {
        var declared = new List<ZipArchiveEntry>();

        using var reader = this.parts.OpenPart(orderingPart, budget);

        while (this.parts.ReadNode(reader, cancellationToken))
        {
            if (reader.NodeType != XmlNodeType.Element
                || reader.LocalName != orderElementName
                || !orderNamespaces.Contains(reader.NamespaceURI))
            {
                continue;
            }

            if (RelationshipIdentifier(reader) is not { } identifier
                || !targets.TryGetValue(identifier, out var page)
                || declared.Count == pageCount)
            {
                return null;
            }

            declared.Add(page);
        }

        return declared;
    }

    /// <summary>Reads the relationship identifier an element names, in whichever conformance class it is written.</summary>
    private static string? RelationshipIdentifier(XmlReader reader) =>
        RelationshipNamespaces
            .Select(namespaceUri => reader.GetAttribute("id", namespaceUri))
            .FirstOrDefault(identifier => identifier is not null);

    /// <summary>Resolves a relationship target against the folder of the part that declared it.</summary>
    private static string ResolvePartName(string folder, string target)
    {
        var resolved = new List<string>();
        var path = target.StartsWith('/') ? target[1..] : folder + target;

        foreach (var segment in path.Split('/'))
        {
            if (segment == "..")
            {
                if (resolved.Count > 0)
                {
                    resolved.RemoveAt(resolved.Count - 1);
                }
            }
            else if (segment is not ("." or ""))
            {
                resolved.Add(segment);
            }
        }

        return string.Join('/', resolved);
    }

    /// <summary>Collects the runs of text in one part, ending a line where the format ends a paragraph.</summary>
    /// <returns><see langword="true" /> when the part carried anything but whitespace; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// <para>
    /// Word-processing and presentation markup differ only in the namespace their text runs are written in, which is
    /// why one walk serves both: <c>t</c> holds the characters and <c>p</c> is what separates them into lines. The
    /// answer is what the part itself carried rather than how long the gathered text grew, because a line break written
    /// between two pages would otherwise read as the second page having said something.
    /// </para>
    /// <para>
    /// A tab and a line break are elements rather than characters here exactly as they are in OpenDocument, so they are
    /// read as the whitespace they stand for — otherwise the invoice line, the table of contents, and the form field
    /// that a tab separates all come back as one joined word.
    /// </para>
    /// <para>
    /// Everything inside a paragraph-properties element is skipped, because that is where the same names mean
    /// something else: <c>tab</c> under <c>pPr</c> declares a tab <em>stop</em> and stands in for no character at all,
    /// so reading it would put a space into every paragraph that overrides the default stops.
    /// </para>
    /// </remarks>
    private bool ReadRunsInto(
        XmlReader reader,
        FrozenSet<string> textNamespaces,
        BoundedTextAccumulator text,
        CancellationToken cancellationToken)
    {
        var insideRun = false;
        var propertiesDepth = -1;
        var carriedText = false;

        while (this.parts.ReadNode(reader, cancellationToken))
        {
            if (propertiesDepth >= 0)
            {
                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == propertiesDepth)
                {
                    propertiesDepth = -1;
                }

                continue;
            }

            switch (reader.NodeType)
            {
                case XmlNodeType.Element when textNamespaces.Contains(reader.NamespaceURI):
                    ReadRunElement(reader, text, ref insideRun, ref propertiesDepth);
                    break;

                case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace:
                    if (insideRun)
                    {
                        text.Add(reader.Value);
                        carriedText |= !string.IsNullOrWhiteSpace(reader.Value);
                    }

                    break;

                case XmlNodeType.EndElement when textNamespaces.Contains(reader.NamespaceURI):
                    if (reader.LocalName == "t")
                    {
                        insideRun = false;
                    }
                    else if (reader.LocalName == "p")
                    {
                        text.EndLine();
                    }

                    break;

                default:
                    break;
            }
        }

        return carriedText;
    }

    /// <summary>Opens a run of characters, writes the whitespace an element stands in for, or enters properties.</summary>
    private static void ReadRunElement(
        XmlReader reader,
        BoundedTextAccumulator text,
        ref bool insideRun,
        ref int propertiesDepth)
    {
        switch (reader.LocalName)
        {
            case "pPr" when !reader.IsEmptyElement:
                propertiesDepth = reader.Depth;
                insideRun = false;
                break;

            case "t":
                insideRun = !reader.IsEmptyElement;
                break;

            case "tab":
                text.Add(" ");
                insideRun = false;
                break;

            case "br" or "cr":
                text.EndLine();
                insideRun = false;
                break;

            default:
                insideRun = false;
                break;
        }
    }

    /// <summary>Reads the workbook's shared string table, which most cells hold their text in.</summary>
    private List<string> ReadSharedStrings(
        ZipArchive archive,
        DecompressionBudget budget,
        CancellationToken cancellationToken)
    {
        var strings = new List<string>();
        var entry = archive.GetEntry(SharedStringsPart);

        if (entry is null)
        {
            return strings;
        }

        // The table is held in memory, so it is bounded by the same ceiling the output is: a workbook whose strings
        // alone pass what one attachment may contribute could not have produced a smaller answer anyway. The entry
        // count is bounded against that number too, because an entry costs a list slot whether or not it carries a
        // character — a table of self-closed entries would otherwise inflate to the container budget while the
        // character ceiling never fired, and no entry past that many could contribute a character the ceiling admits.
        var table = new BoundedTextAccumulator(options.MaxExtractedTextCharacters);
        var item = new StringBuilder();
        var insideRun = false;

        using var reader = this.parts.OpenPart(entry, budget);

        while (this.parts.ReadNode(reader, cancellationToken))
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element when SpreadsheetNamespaces.Contains(reader.NamespaceURI):
                    if (reader.LocalName == "si")
                    {
                        item.Clear();

                        // A self-closed entry raises no end element, so recording it here is what keeps every later
                        // index in the table pointing at its own words rather than at the next entry's.
                        if (reader.IsEmptyElement)
                        {
                            AddBounded(strings, string.Empty, options.MaxExtractedTextCharacters);
                        }
                    }

                    insideRun = reader.LocalName == "t" && !reader.IsEmptyElement;
                    break;

                case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace:
                    if (insideRun)
                    {
                        table.Add(reader.Value);
                        item.Append(reader.Value);
                    }

                    break;

                case XmlNodeType.EndElement when SpreadsheetNamespaces.Contains(reader.NamespaceURI):
                    if (reader.LocalName == "t")
                    {
                        insideRun = false;
                    }
                    else if (reader.LocalName == "si")
                    {
                        AddBounded(strings, item.ToString(), options.MaxExtractedTextCharacters);
                    }

                    break;

                default:
                    break;
            }
        }

        return strings;
    }

    /// <summary>Collects the text every cell of one worksheet holds, one cell to a line.</summary>
    /// <returns><see langword="true" /> when a cell carried anything but whitespace; otherwise <see langword="false" />.</returns>
    private bool ReadCellsInto(
        XmlReader reader,
        List<string> sharedStrings,
        BoundedTextAccumulator text,
        CancellationToken cancellationToken)
    {
        var carriedText = false;

        while (this.parts.ReadNode(reader, cancellationToken))
        {
            if (reader.NodeType != XmlNodeType.Element
                || !SpreadsheetNamespaces.Contains(reader.NamespaceURI)
                || reader.LocalName != "c"
                || reader.IsEmptyElement)
            {
                continue;
            }

            var cellType = reader.GetAttribute("t");

            using var cell = reader.ReadSubtree();

            var value = this.ReadCellText(cell, cellType, sharedStrings, cancellationToken);

            if (!string.IsNullOrEmpty(value))
            {
                text.Add(value);
                text.EndLine();
                carriedText |= !string.IsNullOrWhiteSpace(value);
            }
        }

        return carriedText;
    }

    /// <summary>Reads one cell, which holds its text in the string table, inline, or as a formula's result.</summary>
    /// <remarks>
    /// <para>
    /// A cell whose type is numeric, boolean, an error, or a date yields nothing. Extraction reads what somebody wrote
    /// rather than what a workbook computes: no formula is evaluated, and a number is not text a search should match on.
    /// </para>
    /// <para>
    /// One cell is gathered before any of it is handed on, because what a cell holds is decided by its type once the
    /// whole subtree has been read — so the output ceiling is applied here as well, against each of the two buffers.
    /// Without it a single cell would inflate to whatever the container budget allows before the accumulator ever saw
    /// it, and a cell whose type yields nothing would inflate without ever being measured at all.
    /// </para>
    /// </remarks>
    private string? ReadCellText(
        XmlReader cell,
        string? cellType,
        List<string> sharedStrings,
        CancellationToken cancellationToken)
    {
        var value = new StringBuilder();
        var inlineText = new StringBuilder();
        var insideValue = false;
        var insideInlineRun = false;

        while (this.parts.ReadNode(cell, cancellationToken))
        {
            switch (cell.NodeType)
            {
                case XmlNodeType.Element when SpreadsheetNamespaces.Contains(cell.NamespaceURI):
                    insideValue = cell.LocalName == "v" && !cell.IsEmptyElement;
                    insideInlineRun = cell.LocalName == "t" && !cell.IsEmptyElement;
                    break;

                case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace:
                    if (insideValue)
                    {
                        this.AppendBounded(value, cell.Value);
                    }
                    else if (insideInlineRun)
                    {
                        this.AppendBounded(inlineText, cell.Value);
                    }

                    break;

                case XmlNodeType.EndElement when SpreadsheetNamespaces.Contains(cell.NamespaceURI):
                    insideValue &= cell.LocalName != "v";
                    insideInlineRun &= cell.LocalName != "t";
                    break;

                default:
                    break;
            }
        }

        return cellType switch
        {
            "s" => ResolveSharedString(value.ToString(), sharedStrings),
            "inlineStr" => inlineText.ToString(),
            "str" => value.ToString(),
            _ => null,
        };
    }

    /// <summary>Adds a run of a cell's characters, refusing the cell that grows past the output ceiling.</summary>
    /// <exception cref="AttachmentTextExtractionStoppedException">Thrown when the addition would pass the ceiling.</exception>
    private void AppendBounded(StringBuilder target, string value)
    {
        if (target.Length + value.Length > options.MaxExtractedTextCharacters)
        {
            throw new AttachmentTextExtractionStoppedException(AttachmentTextExtractionOutcome.ExtractedTextTooLarge);
        }

        target.Append(value);
    }

    /// <summary>Records one string-table entry, refusing the table that holds more than the output ceiling allows.</summary>
    /// <exception cref="AttachmentTextExtractionStoppedException">Thrown when the table passes that many entries.</exception>
    private static void AddBounded(List<string> strings, string entry, int maxEntries)
    {
        if (strings.Count >= maxEntries)
        {
            throw new AttachmentTextExtractionStoppedException(AttachmentTextExtractionOutcome.ContainerBoundExceeded);
        }

        strings.Add(entry);
    }

    /// <summary>Resolves the string-table index a cell holds, or nothing when the index names no entry.</summary>
    private static string? ResolveSharedString(string index, List<string> sharedStrings) =>
        int.TryParse(index, NumberStyles.Integer, CultureInfo.InvariantCulture, out var position)
        && position >= 0
        && position < sharedStrings.Count
            ? sharedStrings[position]
            : null;

    /// <summary>Selects the numbered parts one pattern names, in the order their numbers give.</summary>
    /// <remarks>
    /// A part name is written by whoever composed the archive, so the number in one is parsed rather than trusted: a
    /// name carrying more digits than an integer holds orders last instead of throwing.
    /// </remarks>
    private static List<ZipArchiveEntry> OrderedParts(ZipArchive archive, Regex pattern) =>
    [
        .. archive.Entries
            .Select(entry => (entry, match: pattern.Match(entry.FullName)))
            .Where(candidate => candidate.match.Success)
            .OrderBy(candidate => int.TryParse(
                candidate.match.Groups[1].ValueSpan,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var number)
                ? number
                : int.MaxValue)
            .Select(candidate => candidate.entry),
    ];
}
