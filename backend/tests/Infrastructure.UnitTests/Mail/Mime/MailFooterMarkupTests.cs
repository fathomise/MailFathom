// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Delivery.Footers;
using MailFathom.Infrastructure.Mail.Mime;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Mime;

/// <summary>Covers what a footer may be written in: the incoming-mail allow-list, and only images uploaded with it.</summary>
public sealed class MailFooterMarkupTests
{
    private static readonly HashSet<string> Uploaded = new(StringComparer.Ordinal) { "logo.png" };

    private readonly MailFooterMarkup markup = new();

    [Fact]
    public void Sanitize_AnUploadedImage_KeepsItsContentReference()
    {
        // Act
        var reading = this.markup.Sanitize("<p>Anna</p><img src=\"cid:logo.png\" alt=\"Nordwind\">", Uploaded);

        // Assert
        Assert.Null(reading.Refusal);
        Assert.Contains("src=\"cid:logo.png\"", reading.Html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<img src=\"https://example.test/logo.png\">")]
    [InlineData("<img src=\"http://example.test/logo.png\">")]
    [InlineData("<img src=\"data:image/png;base64,iVBORw0KGgo=\">")]
    [InlineData("<img src=\"ftp://example.test/logo.png\">")]
    [InlineData("<img src=\"logo.png\">")]
    [InlineData("<img src=\"cid:logo.png\" srcset=\"https://example.test/logo@2x.png 2x\">")]
    public void Sanitize_AnImageNotUploadedWithTheFooter_IsRefusedAsNotUploaded(string html)
    {
        // Act
        var reading = this.markup.Sanitize(html, Uploaded);

        // Assert
        Assert.Null(reading.Html);
        Assert.Equal(new MailFooterRefusal(MailFooterRefusalReason.ImageNotUploaded), reading.Refusal);
    }

    [Fact]
    public void Sanitize_AReferenceToAnImageNotAmongTheUploads_IsRefusedNamingIt()
    {
        // Act
        var reading = this.markup.Sanitize("<img src=\"cid:portrait.jpg\">", Uploaded);

        // Assert
        Assert.Null(reading.Html);
        Assert.Equal(new MailFooterRefusal(MailFooterRefusalReason.ImageUnknown, "portrait.jpg"), reading.Refusal);
    }

    [Fact]
    public void Sanitize_MarkupTheAllowListRemoves_IsRemoved()
    {
        // Act
        var reading = this.markup.Sanitize("<p onclick=\"steal()\">Anna</p><script>steal()</script>", Uploaded);

        // Assert
        Assert.Null(reading.Refusal);
        Assert.Equal("<p>Anna</p>", reading.Html);
    }

    [Fact]
    public void ReadPlainText_AFootersMarkup_ReadsWhatItDisplays()
    {
        // Act
        var text = this.markup.ReadPlainText("<p>Anna Kowalska</p><p>Head of R&amp;D</p>");

        // Assert
        Assert.Equal("Anna Kowalska\n\nHead of R&D", text.ReplaceLineEndings("\n").Trim());
    }

    [Fact]
    public void EmailHtmlSanitizer_TheMarkAFooterIsWrittenIn_IsKeptOnEveryReadingOfTheSentCopy()
    {
        // Act
        var sanitized = new EmailHtmlSanitizer().Sanitize("<p>Hello</p><footer><p>Anna</p></footer>");

        // Assert
        Assert.Equal("<p>Hello</p><footer><p>Anna</p></footer>", sanitized);
    }

    [Fact]
    public void EmailHtmlSanitizer_IncomingMail_StillDropsContentReferences()
    {
        // Act
        var sanitized = new EmailHtmlSanitizer().Sanitize("<img src=\"cid:logo.png\" alt=\"Nordwind\">");

        // Assert
        Assert.DoesNotContain("cid:", sanitized, StringComparison.Ordinal);
    }
}
