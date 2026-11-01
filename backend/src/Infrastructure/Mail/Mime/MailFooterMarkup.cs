// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using MailFathom.Application.Mail.Delivery.Footers;

namespace MailFathom.Infrastructure.Mail.Mime;

/// <summary>Reads a footer's markup through the allow-list incoming mail is read through, keeping its own images.</summary>
/// <remarks>
/// The images are checked on the markup as it was written, before it is sanitized, because the sanitizer would only
/// drop a remote reference — and a footer whose logo silently disappeared is one its writer never finds out about.
/// What is refused is the markup's own <c>img</c> elements; every other way a body points somewhere is removed by the
/// sanitizer like it is from any message.
/// </remarks>
internal sealed class MailFooterMarkup : IMailFooterMarkup
{
    private const string ContentReferenceScheme = "cid:";

    private readonly EmailHtmlSanitizer sanitizer = EmailHtmlSanitizer.KeepingContentReferences();

    /// <inheritdoc />
    public MailFooterMarkupReading Sanitize(string html, IReadOnlySet<string> imageNames)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(imageNames);

        if (FindImageRefusal(html, imageNames) is { } refusal)
        {
            return new MailFooterMarkupReading(null, refusal);
        }

        return new MailFooterMarkupReading(this.sanitizer.Sanitize(html), null);
    }

    /// <inheritdoc />
    public string ReadPlainText(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        return HtmlBodyTextReader.ReadDisplayedText(html, MailFooterContent.MaximumHtmlLength);
    }

    /// <summary>Reads the name a <c>cid:</c> reference names, or <see langword="null" /> for any other reference.</summary>
    /// <param name="source">An image's <c>src</c> as it was written.</param>
    /// <returns>The name.</returns>
    internal static string? ContentReferenceName(string? source)
    {
        var trimmed = source?.Trim();

        return trimmed is not null && trimmed.StartsWith(ContentReferenceScheme, StringComparison.OrdinalIgnoreCase)
            ? trimmed[ContentReferenceScheme.Length..]
            : null;
    }

    private static MailFooterRefusal? FindImageRefusal(string html, IReadOnlySet<string> imageNames)
    {
        using var document = new HtmlParser().ParseDocument(html);

        return document.Images
            .Select(image => RefusalOf(image, imageNames))
            .FirstOrDefault(static refusal => refusal is not null);
    }

    private static MailFooterRefusal? RefusalOf(IHtmlImageElement image, IReadOnlySet<string> imageNames)
    {
        if (image.HasAttribute("srcset") || ContentReferenceName(image.GetAttribute("src")) is not { } name)
        {
            return new MailFooterRefusal(MailFooterRefusalReason.ImageNotUploaded);
        }

        return imageNames.Contains(name)
            ? null
            : new MailFooterRefusal(
                MailFooterRefusalReason.ImageUnknown,
                MailFooterContent.IsImageName(name) ? name : null);
    }
}
