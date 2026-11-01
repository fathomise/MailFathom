// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text;
using AngleSharp.Html.Parser;
using MailFathom.Application.Mail.Delivery.Footers;
using MimeKit;
using MimeKit.Utils;

namespace MailFathom.Infrastructure.Mail.Mime.Composition;

/// <summary>Puts a footer into a composed message with MimeKit, under the author's words and above what they quote.</summary>
/// <remarks>
/// <para>
/// The message is parsed again rather than recomposed, because what arrives here is whatever the outbox was handed —
/// a message composed a moment ago, a draft stored weeks ago, or one occasion of a recurring send — and recomposing
/// any of them would change the identity, the date, and the threading it already carries. Only the two body parts
/// change, and the headers are written back exactly as they were read.
/// </para>
/// <para>
/// Where the author's words end is found from what the composition left: in the markup, the comment written in front
/// of a quotation; in the text, which carries no comment, the quotation's own shape — a blank line, the attribution
/// ending in a colon, and nothing after it but quoted lines — and only in a message that answers another, since only
/// an answer quotes anything.
/// </para>
/// </remarks>
internal sealed class MimeKitMailFooterComposer : IMailFooterComposer
{
    /// <summary>The line RFC 3676 opens a signature with, followed by its line break.</summary>
    internal const string SignatureDelimiter = "-- \n";

    /// <summary>The domain a content identifier is minted under where the message names no author's domain.</summary>
    private const string FallbackContentIdDomain = "footer.mailfathom.invalid";

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Append(ReadOnlyMemory<byte> rawMime, MailFooter footer)
    {
        ArgumentNullException.ThrowIfNull(footer);

        using var source = new MemoryStream(rawMime.ToArray(), writable: false);
        using var message = MimeMessage.Load(source);

        if (message.Body is MultipartReport)
        {
            return rawMime;
        }

        var text = FindBodyPart(message.Body, parent: null, index: 0, html: false);
        var html = FindBodyPart(message.Body, parent: null, index: 0, html: true);

        if (text is null && html is null)
        {
            return rawMime;
        }

        if (text is { } textPart)
        {
            var answers = message.Headers.Contains(HeaderId.InReplyTo);

            textPart.Part.SetText(Encoding.UTF8, WithTextFooter(textPart.Part.Text, footer.PlainText, answers));
        }

        if (html is { } htmlPart)
        {
            var contentIds = MintContentIds(footer, message);

            htmlPart.Part.SetText(
                Encoding.UTF8,
                WithHtmlFooter(htmlPart.Part.Text, Referencing(footer.Html, contentIds)));

            Embed(message, htmlPart, footer.Images, contentIds);
        }

        return Serialize(message);
    }

    /// <inheritdoc />
    public ReadOnlyMemory<byte> ComposeAlone(MailFooter footer)
    {
        ArgumentNullException.ThrowIfNull(footer);

        var contentIds = footer.Images.ToDictionary(
            static image => image.Name,
            static _ => MimeUtils.GenerateMessageId(FallbackContentIdDomain),
            StringComparer.Ordinal);

        var markup = new TextPart("html");
        markup.SetText(Encoding.UTF8, FooterBlock(Referencing(footer.Html, contentIds)));

        using var message = new MimeMessage { Body = markup };
        Embed(message, new BodyPart(null, 0, markup), footer.Images, contentIds);

        return Serialize(message);
    }

    /// <summary>Finds the text or the markup an author wrote, skipping every attachment and every message a part encloses.</summary>
    private static BodyPart? FindBodyPart(MimeEntity? entity, Multipart? parent, int index, bool html) => entity switch
    {
        TextPart part when !part.IsAttachment && (html ? part.IsHtml : part.IsPlain) => new BodyPart(parent, index, part),
        Multipart multipart => multipart
            .Select((child, position) => FindBodyPart(child, multipart, position, html))
            .FirstOrDefault(static found => found is not null),
        _ => null,
    };

    private static string WithTextFooter(string text, string footerText, bool answers)
    {
        var body = text.ReplaceLineEndings("\n").TrimEnd('\n');
        var quotationStart = answers ? FindTextQuotation(body) : body.Length;

        var footed = string.Concat(
            body[..quotationStart].TrimEnd('\n'),
            "\n\n",
            SignatureDelimiter,
            footerText.ReplaceLineEndings("\n").Trim('\n'),
            body[quotationStart..]);

        return footed.ReplaceLineEndings("\r\n");
    }

    /// <summary>Finds where the quotation an answer carries begins in its text, or the end of the text where it carries none.</summary>
    /// <remarks>
    /// A quoted line always opens with a marker, so no blank line can fall inside a quotation and the last blank line
    /// in the text is the only place one can begin. What follows it has to be the attribution and then quoted lines to
    /// the end; the last line may be cut short by the bound the quotation was composed under.
    /// </remarks>
    internal static int FindTextQuotation(string body)
    {
        var separator = body.LastIndexOf("\n\n", StringComparison.Ordinal);
        if (separator < 0)
        {
            return body.Length;
        }

        var lines = body[(separator + 2)..].Split('\n');

        var isQuotation = lines.Length > 1
            && lines[0].EndsWith(':')
            && lines.Skip(1).SkipLast(1).All(static line => line.StartsWith('>'))
            && (lines[^1].Length == 0 || lines[^1].StartsWith('>'));

        return isQuotation ? separator : body.Length;
    }

    private static string WithHtmlFooter(string html, string footerHtml)
    {
        var position = html.LastIndexOf(IMailFooterComposer.HtmlQuotationMarker, StringComparison.Ordinal);

        if (position < 0)
        {
            position = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        }

        return html.Insert(position < 0 ? html.Length : position, FooterBlock(footerHtml)).ReplaceLineEndings("\r\n");
    }

    /// <summary>Wraps a footer in the element a later reading of the sent copy finds it by.</summary>
    /// <remarks>
    /// <c>footer</c> is an element the incoming-mail sanitizer keeps and the reduction already reads as a block, so the
    /// mark survives every reading of the sent copy without a class or an attribute the sanitizer would strip.
    /// </remarks>
    private static string FooterBlock(string footerHtml) => $"<footer>{footerHtml}</footer>";

    /// <summary>Points each image reference in the footer at the content identifier minted for this message.</summary>
    private static string Referencing(string footerHtml, Dictionary<string, string> contentIds)
    {
        using var document = new HtmlParser().ParseDocument(footerHtml);

        foreach (var image in document.Images)
        {
            if (MailFooterMarkup.ContentReferenceName(image.GetAttribute("src")) is { } name
                && contentIds.TryGetValue(name, out var contentId))
            {
                image.SetAttribute("src", $"cid:{contentId}");
            }
        }

        return document.Body?.InnerHtml ?? string.Empty;
    }

    private static Dictionary<string, string> MintContentIds(MailFooter footer, MimeMessage message)
    {
        var domain = message.From.Mailboxes.FirstOrDefault()?.Domain is { Length: > 0 } authorDomain
            ? authorDomain
            : FallbackContentIdDomain;

        return footer.Images.ToDictionary(
            static image => image.Name,
            _ => MimeUtils.GenerateMessageId(domain),
            StringComparer.Ordinal);
    }

    /// <summary>Embeds the footer's images beside the markup, in the <c>multipart/related</c> the markup is the root of.</summary>
    private static void Embed(
        MimeMessage message,
        BodyPart markup,
        IReadOnlyList<MailFooterImage> images,
        Dictionary<string, string> contentIds)
    {
        if (images.Count == 0)
        {
            return;
        }

        var related = markup.Parent as MultipartRelated;

        if (related is null)
        {
            related = new MultipartRelated { markup.Part };

            if (markup.Parent is { } parent)
            {
                parent[markup.Index] = related;
            }
            else
            {
                message.Body = related;
            }
        }

        foreach (var image in images)
        {
            related.Add(new MimePart(ContentType.Parse(image.MediaType))
            {
                Content = new MimeContent(new MemoryStream(image.Content.ToArray(), writable: false)),
                ContentId = contentIds[image.Name],
                ContentDisposition = new ContentDisposition(ContentDisposition.Inline),
                ContentTransferEncoding = ContentEncoding.Base64,
                FileName = image.Name,
            });
        }
    }

    /// <summary>Writes the message the way a composed one is written: seven bits, and the line ending SMTP requires.</summary>
    private static byte[] Serialize(MimeMessage message)
    {
        var format = FormatOptions.Default.Clone();
        format.NewLineFormat = NewLineFormat.Dos;
        format.EnsureNewLine = true;

        message.Prepare(EncodingConstraint.SevenBit, format.MaxLineLength);

        using var written = new MemoryStream();
        message.WriteTo(format, written);

        return written.ToArray();
    }

    /// <summary>One body part and where it sits, so it can be replaced by a <c>multipart/related</c> holding it.</summary>
    private sealed record BodyPart(Multipart? Parent, int Index, TextPart Part);
}
