// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text;
using MailFathom.Application.Mail.Delivery.Footers;
using MailFathom.Infrastructure.Mail.Dkim;
using MailFathom.Infrastructure.Mail.Mime.Composition;
using MimeKit;
using MimeKit.Cryptography;
using NSubstitute;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Mime.Composition;

/// <summary>Covers where a footer goes in a composed message: under the author's words, above what they quote, once in each part.</summary>
public sealed class MimeKitMailFooterComposerTests
{
    private static readonly byte[] Logo = [137, 80, 78, 71, 13, 10, 26, 10];

    private static readonly MailFooter Footer = new(MailFooterSource.Own, "<p>Anna Kowalska</p>", "Anna Kowalska", []);

    private readonly MimeKitMailFooterComposer composer = new();

    [Fact]
    public void Append_ANewMessage_PutsTheFooterAtTheEndOfBothPartsMarkedOnce()
    {
        // Arrange
        var rawMime = Composed("Dzień dobry.", "<html><body><p>Dzień dobry.</p></body></html>");

        // Act
        using var footed = Parse(this.composer.Append(rawMime, Footer));

        // Assert
        Assert.Equal("Dzień dobry.\n\n-- \nAnna Kowalska", footed.TextBody);
        Assert.Equal("<html><body><p>Dzień dobry.</p><footer><p>Anna Kowalska</p></footer></body></html>", footed.HtmlBody);
    }

    [Fact]
    public void Append_AReply_PutsTheFooterAboveTheQuotationInBothParts()
    {
        // Arrange
        var text = "Thank you.\n\nOn 2026-08-17 09:30 UTC, bob@example.test wrote:\n> The report is attached.\n> Regards";
        var html = $"<p>Thank you.</p>{IMailFooterComposer.HtmlQuotationMarker}<p>On 2026-08-17 09:30 UTC, bob@example.test wrote:</p><blockquote><p>The report is attached.</p></blockquote>";
        var rawMime = Composed(text, html, inReplyTo: "original@example.test");

        // Act
        using var footed = Parse(this.composer.Append(rawMime, Footer));

        // Assert
        Assert.Equal(
            "Thank you.\n\n-- \nAnna Kowalska\n\nOn 2026-08-17 09:30 UTC, bob@example.test wrote:\n> The report is attached.\n> Regards",
            footed.TextBody);
        Assert.Equal(
            $"<p>Thank you.</p><footer><p>Anna Kowalska</p></footer>{IMailFooterComposer.HtmlQuotationMarker}<p>On 2026-08-17 09:30 UTC, bob@example.test wrote:</p><blockquote><p>The report is attached.</p></blockquote>",
            footed.HtmlBody);
    }

    [Fact]
    public void Append_ANewMessageEndingInLinesThatLookQuoted_PutsTheFooterAtTheEnd()
    {
        // Arrange
        var rawMime = Composed("Notes:\n\nAgenda:\n> item one", html: null);

        // Act
        using var footed = Parse(this.composer.Append(rawMime, Footer));

        // Assert
        Assert.Equal("Notes:\n\nAgenda:\n> item one\n\n-- \nAnna Kowalska", footed.TextBody?.TrimEnd('\n'));
        Assert.Null(footed.HtmlBody);
    }

    [Fact]
    public void Append_AFooterWithAnImage_EmbedsItBesideTheMarkupAndPointsTheReferenceAtIt()
    {
        // Arrange
        var footer = new MailFooter(
            MailFooterSource.Own,
            "<p>Anna</p><img src=\"cid:logo.png\" alt=\"Nordwind\">",
            "Anna",
            [new MailFooterImage("logo.png", "image/png", Logo)]);
        var rawMime = Composed("Dzień dobry.", "<p>Dzień dobry.</p>");

        // Act
        using var footed = Parse(this.composer.Append(rawMime, footer));

        // Assert
        var related = Assert.IsType<MultipartRelated>(Assert.IsType<MultipartAlternative>(footed.Body)[1]);
        var image = Assert.Single(related.OfType<MimePart>(), part => part is not TextPart);
        Assert.Equal("image/png", image.ContentType.MimeType);
        Assert.Equal(Logo, ReadContent(image));
        Assert.Contains($"<img src=\"cid:{image.ContentId}\" alt=\"Nordwind\">", footed.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("cid:logo.png", footed.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Append_AReadReceipt_LeavesItAsComposed()
    {
        // Arrange
        using var message = new MimeMessage
        {
            Subject = "Read: Quarterly figures",
            Body = new MultipartReport("disposition-notification") { new TextPart("plain") { Text = "Read." } },
        };
        message.From.Add(MailboxAddress.Parse("anna@example.test"));
        var rawMime = Serialize(message);

        // Act
        var footed = this.composer.Append(rawMime, Footer);

        // Assert
        Assert.Equal(rawMime, footed.ToArray());
    }

    [Fact]
    public void ComposeAlone_AFooterWithAnImage_IsTheMarkedMarkupBesideItsImage()
    {
        // Arrange
        var footer = new MailFooter(
            MailFooterSource.Organization,
            "<p>Anna</p><img src=\"cid:logo.png\">",
            "Anna",
            [new MailFooterImage("logo.png", "image/png", Logo)]);

        // Act
        using var composed = Parse(this.composer.ComposeAlone(footer));

        // Assert
        var related = Assert.IsType<MultipartRelated>(composed.Body);
        var image = Assert.Single(related.OfType<MimePart>(), part => part is not TextPart);
        Assert.Equal($"<footer><p>Anna</p><img src=\"cid:{image.ContentId}\"></footer>", composed.HtmlBody);
    }

    [Fact]
    public void Append_AMessageItsOrganizationThenSigns_StillVerifies()
    {
        // Arrange
        var privateKey = new Ed25519PrivateKeyParameters(new SecureRandom());
        var keyId = new Guid("0199a0c0-0000-7000-8000-0000000000a1");
        var rawMime = Composed("Dzień dobry.", "<p>Dzień dobry.</p>");

        // Act
        var footed = this.composer.Append(rawMime, Footer);
        var signed = OrganizationMailSigner.Sign(footed, keyId, privateKey.GetEncoded());

        // Assert
        Assert.NotNull(signed);
        using var message = Parse(signed);
        var signature = Assert.Single(message.Headers, header => header.Field == OrganizationMailSigner.SignatureHeaderName);
        var locator = Substitute.For<IDkimPublicKeyLocator>();
        locator
            .LocatePublicKey(Arg.Any<string>(), "example.test", keyId.ToString("D"), Arg.Any<CancellationToken>())
            .Returns(privateKey.GeneratePublicKey());
        Assert.True(new DkimVerifier(locator).Verify(
            message,
            new Header(HeaderId.DkimSignature, signature.Value),
            TestContext.Current.CancellationToken));
        Assert.Contains("<footer>", message.HtmlBody, StringComparison.Ordinal);
    }

    private static byte[] Composed(string text, string? html, string? inReplyTo = null)
    {
        var plain = new TextPart("plain");
        plain.SetText(Encoding.UTF8, text);

        MimeEntity body = plain;

        if (html is not null)
        {
            var markup = new TextPart("html");
            markup.SetText(Encoding.UTF8, html);
            body = new MultipartAlternative { plain, markup };
        }

        using var message = new MimeMessage
        {
            Subject = "Quarterly figures",
            Date = new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero),
            MessageId = "composed-1@example.test",
            Body = body,
        };
        message.From.Add(MailboxAddress.Parse("anna@example.test"));
        message.To.Add(MailboxAddress.Parse("bob@example.test"));

        if (inReplyTo is not null)
        {
            message.InReplyTo = inReplyTo;
        }

        return Serialize(message);
    }

    private static byte[] Serialize(MimeMessage message)
    {
        var format = FormatOptions.Default.Clone();
        format.NewLineFormat = NewLineFormat.Dos;

        using var written = new MemoryStream();
        message.WriteTo(format, written);

        return written.ToArray();
    }

    private static MimeMessage Parse(ReadOnlyMemory<byte> rawMime)
    {
        using var source = new MemoryStream(rawMime.ToArray(), writable: false);

        return MimeMessage.Load(source);
    }

    private static byte[] ReadContent(MimePart part)
    {
        Assert.NotNull(part.Content);
        using var content = new MemoryStream();
        part.Content.DecodeTo(content);

        return content.ToArray();
    }
}
