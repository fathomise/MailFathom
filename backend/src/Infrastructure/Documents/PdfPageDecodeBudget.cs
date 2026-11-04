// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Extraction.Attachments;

namespace MailFathom.Infrastructure.Documents;

/// <summary>Counts what a PDF's streams decode to while one page is read, and refuses a page that decodes past its allowance.</summary>
/// <remarks>
/// <para>
/// The allowance is per page rather than per document, which is where this differs from
/// <see cref="DecompressionBudget" />. The library decodes a form drawn on a page every time the page draws it, so a
/// letterhead repeated over three hundred pages is decoded three hundred times; a total over the whole document would
/// refuse that ordinary file, while what a hostile one needs — one page decoding without limit — is what a page's own
/// allowance stops. What the document decodes while it is opened counts towards its first page.
/// </para>
/// <para>
/// The library swallows some of its own failures — a font it cannot load is skipped rather than reported — so a refusal
/// raised from inside one of its calls may never reach this adapter. The count is left past the allowance when that
/// happens, which is what <see cref="EndPage" /> reads: the refusal stopped the decode it was raised from either way, and
/// the page is still refused rather than read with whatever the library dropped.
/// </para>
/// </remarks>
internal sealed class PdfPageDecodeBudget(long maxOctets)
{
    private long consumed;

    /// <summary>Records octets a filter is about to produce, before it produces them.</summary>
    /// <param name="octets">How many it will produce.</param>
    /// <exception cref="AttachmentTextExtractionStoppedException">Thrown when the page passes its allowance.</exception>
    public void Consume(long octets)
    {
        this.consumed += octets;

        this.ThrowIfExceeded();
    }

    /// <summary>Closes the page just read and opens the next one's allowance.</summary>
    /// <exception cref="AttachmentTextExtractionStoppedException">Thrown when the page passed its allowance and the library swallowed the refusal.</exception>
    public void EndPage()
    {
        this.ThrowIfExceeded();

        this.consumed = 0;
    }

    private void ThrowIfExceeded()
    {
        if (this.consumed > maxOctets)
        {
            throw new AttachmentTextExtractionStoppedException(AttachmentTextExtractionOutcome.ContainerBoundExceeded);
        }
    }
}
