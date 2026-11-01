// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using MailFathom.Application.Mail.Delivery.Footers;

namespace MailFathom.Infrastructure.Persistence.Footers;

/// <summary>A footer or a template as the <c>jsonb</c> document its row holds.</summary>
/// <param name="Html">The sanitized markup.</param>
/// <param name="Images">The images, their octets written as base64.</param>
internal sealed record MailFooterDocument(string Html, IReadOnlyList<MailFooterDocumentImage> Images)
{
    /// <summary>Writes a footer as its document.</summary>
    /// <param name="footer">The footer.</param>
    /// <returns>The document's JSON.</returns>
    public static string Render(MailFooterContent footer) =>
        JsonSerializer.Serialize(
            new MailFooterDocument(
                footer.Html,
                [.. footer.Images.Select(static image => new MailFooterDocumentImage(image.Name, image.MediaType, image.Content.ToArray()))]),
            MailFooterDocumentJsonContext.Default.MailFooterDocument);

    /// <summary>Reads a document back into the footer it was written from.</summary>
    /// <param name="json">The document's JSON.</param>
    /// <returns>The footer.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the document is not one this build writes.</exception>
    public static MailFooterContent Parse(string json)
    {
        var document = JsonSerializer.Deserialize(json, MailFooterDocumentJsonContext.Default.MailFooterDocument)
            ?? throw new InvalidOperationException("A stored footer document is empty.");

        return new MailFooterContent(
            document.Html,
            [.. document.Images.Select(static image => new MailFooterImage(image.Name, image.MediaType, image.Content))]);
    }
}
