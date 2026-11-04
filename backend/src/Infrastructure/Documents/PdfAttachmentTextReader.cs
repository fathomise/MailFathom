// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Extraction.Attachments;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace MailFathom.Infrastructure.Documents;

/// <summary>Reads a PDF's text, one page at a time, and records which of its pages carry none.</summary>
/// <remarks>
/// <para>
/// The library reads structure and text and executes nothing. A PDF may declare JavaScript, an embedded file, an
/// action on opening, and a form that submits somewhere; none of them is evaluated, followed, or fetched, because
/// nothing here asks the document for anything but its pages' characters.
/// </para>
/// <para>
/// Pages are read one at a time rather than through the whole document at once, so the cancellation the extractor's
/// timeout raises is observed between them and the output ceiling stops the read where it is reached. A page whose
/// content is an image carries no characters, and is recorded as such rather than passed over — that is the exact page
/// a later optical-character-recognition pass would be given.
/// </para>
/// <para>
/// What a page's streams decode to is bounded while they decode, by
/// <see cref="AttachmentTextExtractionOptions.MaxPdfPageDecodedOctets" />. The library inflates streams itself, so the
/// bound is applied through the filters it is handed: <see cref="BoundedPdfFilterProvider" /> charges each stream to the
/// page's <see cref="PdfPageDecodeBudget" /> before the library's decoder produces it, and a page past its allowance is
/// abandoned as <see cref="AttachmentTextExtractionOutcome.ContainerBoundExceeded" />. That is also what bounds the work
/// inside one page, which the timeout cannot interrupt. The archive readers' inflation total, per-part ratio, and
/// element depth do not reach here: the document is not an archive of markup, and the library still builds a page's
/// whole text before the output ceiling below can refuse it.
/// </para>
/// <para>
/// The one part-count bound that does reach here is the page count itself, refused against the same
/// <see cref="AttachmentTextExtractionOptions.MaxContainerParts" /> the archive readers hold their pages to: a page
/// costs a recorded boundary whether or not it carried a word, so the number of them a sender may declare is bounded
/// before the walk rather than left to the input ceiling.
/// </para>
/// <para>
/// A page carrying no text layer is where a scanner put a picture of the page, so where the deployment reads pictures
/// the images drawn on such a page are kept for a model to read. A page that does carry text keeps its text and nothing
/// else: a searchable scan already holds its words there, and reading the page image again would send a picture to say
/// what the document already said. Only an image stored as a JPEG stream is kept, because that stream is an image file
/// as it stands; every other filter would have to be decoded here first, and nothing on this path decodes a picture.
/// </para>
/// </remarks>
internal sealed partial class PdfAttachmentTextReader(AttachmentTextExtractionOptions options, ILogger logger)
{
    /// <summary>Reads one PDF attachment.</summary>
    /// <param name="content">The attachment's octets, positioned at the start.</param>
    /// <param name="cancellationToken">Cancels the read between pages.</param>
    /// <returns>What the document yielded.</returns>
    /// <exception cref="AttachmentTextExtractionStoppedException">Thrown when the output ceiling is crossed, when the document declares more pages than one container may hold, or when a page's streams decode past what one page may.</exception>
    public ExtractedAttachmentText Read(Stream content, CancellationToken cancellationToken)
    {
        var text = new BoundedTextAccumulator(options.MaxExtractedTextCharacters);
        var pagesWithoutText = new List<int>();
        var segments = new List<AttachmentTextSegment>();
        var pictures = new EmbeddedPictureCollector(options.MaxPicturesPerDocument);
        var pageDecodeBudget = new PdfPageDecodeBudget(options.MaxPdfPageDecodedOctets);

        using var document = PdfDocument.Open(content, ReadOnlyParsingOptions(pageDecodeBudget));

        // How many pages the document declares is the sender's, and one segment is recorded per page below, so without
        // a ceiling a file well inside the input bound could name hundreds of thousands of near-empty page objects and
        // have every one of them stored in this attachment's boundary document. The archive readers bound their own
        // page counts by the same number, each of their pages being a part of the container.
        if (document.NumberOfPages > options.MaxContainerParts)
        {
            throw new AttachmentTextExtractionStoppedException(
                AttachmentTextExtractionOutcome.ContainerBoundExceeded);
        }

        for (var page = 1; page <= document.NumberOfPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Recorded before the page's characters are added, and for every page rather than only the ones that
            // carried words: the boundary is what a later passage's offset is resolved against, and a page skipped
            // here would move every passage after it onto a page it was not read from.
            segments.Add(new AttachmentTextSegment(AttachmentTextSegmentKind.Page, page, Label: null, text.Length));

            var pdfPage = document.GetPage(page);
            var pageText = ContentOrderTextExtractor.GetText(pdfPage);

            if (string.IsNullOrWhiteSpace(pageText))
            {
                pagesWithoutText.Add(page);
                this.OfferPictures(pdfPage, page, pictures, cancellationToken);
            }
            else
            {
                text.Add(pageText);
                text.EndLine();
            }

            pageDecodeBudget.EndPage();
        }

        // A refusal the library swallowed while the document opened has no page to be read at the end of when the
        // document then declares none, so the allowance is read once more before anything is returned.
        pageDecodeBudget.EndPage();

        return new ExtractedAttachmentText(text.ToText(), document.NumberOfPages, pagesWithoutText, segments)
        {
            Pictures = pictures.Pictures,
        };
    }

    /// <summary>Offers every image drawn on one page, as the document stores it, to the collector.</summary>
    /// <remarks>
    /// <para>
    /// The allowance and the caller's deadline are both checked per image rather than per page, because the number of
    /// images one page draws is the sender's and nothing else bounds it.
    /// </para>
    /// <para>
    /// A page whose images the library cannot enumerate keeps the rest of the document readable: losing one scanned page
    /// is a smaller loss than reporting a document whose text layer read cleanly as malformed. The failure is logged by
    /// its type alone, since a parser's message can quote the document it was reading.
    /// </para>
    /// </remarks>
    private void OfferPictures(
        Page page,
        int pageNumber,
        EmbeddedPictureCollector pictures,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var image in page.GetImages())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!pictures.WantsMore)
                {
                    return;
                }

                pictures.Offer(pageNumber, image.RawMemory.Span);
            }
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            LogPicturesUnreadable(logger, pageNumber, failure.GetType().Name);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The images on page {PageNumber} of a PDF attachment could not be read ({FailureType}); that page's pictures are not sent to the describer, and the rest of the document is read as usual.")]
    private static partial void LogPicturesUnreadable(ILogger logger, int pageNumber, string failureType);

    /// <summary>Builds the options one PDF is opened under.</summary>
    /// <param name="pageDecodeBudget">The allowance every stream the document decodes is charged to.</param>
    /// <remarks>
    /// The two lenient settings widen what is readable rather than what is trusted. Real mail carries PDFs written by
    /// generators that disagree with the specification in small ways, and a strict parse would report a contract
    /// somebody can open in their reader as unreadable; a font the document names and does not embed is a defect in the
    /// document's presentation rather than a reason to abandon its words. Neither relaxes a bound: skipping a font the
    /// library could not load also swallows a refusal raised while it was decoded, and the page is refused at its end
    /// regardless, as <see cref="PdfPageDecodeBudget" /> describes.
    /// </remarks>
    private static ParsingOptions ReadOnlyParsingOptions(PdfPageDecodeBudget pageDecodeBudget) => new()
    {
        UseLenientParsing = true,
        SkipMissingFonts = true,
        FilterProvider = new BoundedPdfFilterProvider(pageDecodeBudget),
    };
}
