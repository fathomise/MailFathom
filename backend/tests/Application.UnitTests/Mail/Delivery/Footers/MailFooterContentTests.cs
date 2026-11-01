// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Delivery.Footers;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Footers;

public sealed class MailFooterContentTests
{
    private static readonly MailFootings.VerbatimMailFooterMarkup Markup = new();

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];

    [Fact]
    public void Read_AFooterWithinEveryBound_KeepsTheSanitizedMarkupAndItsImages()
    {
        // Arrange
        MailFooterImage[] images = [Image("logo.png")];

        // Act
        var footer = MailFooterContent.Read("<p>Anna</p><img src=\"cid:logo.png\">", images, Markup, out var refusal);

        // Assert
        Assert.Null(refusal);
        Assert.NotNull(footer);
        Assert.Equal("<p>Anna</p><img src=\"cid:logo.png\">", footer.Html);
        Assert.Equal(images, footer.Images);
    }

    [Fact]
    public void Read_MarkupLongerThanTheBound_IsRefused()
    {
        // Act
        var footer = MailFooterContent.Read(
            new string('a', MailFooterContent.MaximumHtmlLength + 1),
            [],
            Markup,
            out var refusal);

        // Assert
        Assert.Null(footer);
        Assert.Equal(new MailFooterRefusal(MailFooterRefusalReason.MarkupTooLong), refusal);
    }

    [Fact]
    public void Read_MoreImagesThanTheBound_IsRefused()
    {
        // Arrange
        MailFooterImage[] images =
            [.. Enumerable.Range(0, MailFooterContent.MaximumImageCount + 1).Select(index => Image($"image-{index}.png"))];

        // Act
        var footer = MailFooterContent.Read("<p>Anna</p>", images, Markup, out var refusal);

        // Assert
        Assert.Null(footer);
        Assert.Equal(new MailFooterRefusal(MailFooterRefusalReason.TooManyImages), refusal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".hidden")]
    [InlineData("logo png")]
    [InlineData("logo\"png")]
    [InlineData("logo\n")]
    public void Read_AnImageNameACidReferenceCannotCarry_IsRefused(string name)
    {
        // Act
        var footer = MailFooterContent.Read("<p>Anna</p>", [Image(name)], Markup, out var refusal);

        // Assert
        Assert.Null(footer);
        Assert.Equal(new MailFooterRefusal(MailFooterRefusalReason.ImageNameNotAccepted), refusal);
    }

    [Fact]
    public void Read_TwoImagesUnderOneName_IsRefusedNamingIt()
    {
        // Act
        var footer = MailFooterContent.Read("<p>Anna</p>", [Image("logo.png"), Image("logo.png")], Markup, out var refusal);

        // Assert
        Assert.Null(footer);
        Assert.Equal(new MailFooterRefusal(MailFooterRefusalReason.ImageNameRepeated, "logo.png"), refusal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(MailFooterContent.MaximumImageBytes + 1)]
    public void Read_AnEmptyOrOversizedImage_IsRefusedNamingIt(int length)
    {
        // Arrange
        var image = new MailFooterImage("logo.png", "image/png", new byte[length]);

        // Act
        var footer = MailFooterContent.Read("<p>Anna</p>", [image], Markup, out var refusal);

        // Assert
        Assert.Null(footer);
        Assert.Equal(new MailFooterRefusal(MailFooterRefusalReason.ImageSizeNotAccepted, "logo.png"), refusal);
    }

    [Theory]
    [InlineData("image/svg+xml")]
    [InlineData("image/webp")]
    [InlineData("text/html")]
    public void Read_AnImageTypeEveryClientDoesNotDraw_IsRefusedNamingIt(string mediaType)
    {
        // Arrange
        var image = new MailFooterImage("logo", mediaType, Png);

        // Act
        var footer = MailFooterContent.Read("<p>Anna</p>", [image], Markup, out var refusal);

        // Assert
        Assert.Null(footer);
        Assert.Equal(new MailFooterRefusal(MailFooterRefusalReason.ImageTypeNotAccepted, "logo"), refusal);
    }

    [Theory]
    [InlineData("image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })]
    [InlineData("IMAGE/JPEG", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 })]
    [InlineData("image/gif", new byte[] { 0x47, 0x49, 0x46, 0x38, 0x37, 0x61 })]
    [InlineData("image/gif", new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 })]
    public void Read_AnImageOpeningAsTheTypeItStates_IsKept(string mediaType, byte[] content)
    {
        // Act
        var footer = MailFooterContent.Read("<p>Anna</p>", [new MailFooterImage("logo", mediaType, content)], Markup, out var refusal);

        // Assert
        Assert.Null(refusal);
        Assert.NotNull(footer);
    }

    [Theory]
    [InlineData("image/png", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 })]
    [InlineData("image/jpeg", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })]
    [InlineData("image/gif", new byte[] { 0x3C, 0x73, 0x76, 0x67, 0x3E })]
    [InlineData("image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47 })]
    public void Read_AnImageWhoseOctetsAreNotTheTypeItStates_IsRefusedNamingIt(string mediaType, byte[] content)
    {
        // Act
        var footer = MailFooterContent.Read("<p>Anna</p>", [new MailFooterImage("logo", mediaType, content)], Markup, out var refusal);

        // Assert
        Assert.Null(footer);
        Assert.Equal(new MailFooterRefusal(MailFooterRefusalReason.ImageTypeNotAccepted, "logo"), refusal);
    }

    [Fact]
    public void Read_MarkupTheSanitizerRefuses_CarriesItsRefusal()
    {
        // Arrange
        var markup = new RefusingMarkup(new MailFooterRefusal(MailFooterRefusalReason.ImageNotUploaded, "https://example.test/logo.png"));

        // Act
        var footer = MailFooterContent.Read("<img src=\"https://example.test/logo.png\">", [], markup, out var refusal);

        // Assert
        Assert.Null(footer);
        Assert.Equal(new MailFooterRefusal(MailFooterRefusalReason.ImageNotUploaded, "https://example.test/logo.png"), refusal);
    }

    private static MailFooterImage Image(string name) => new(name, "image/png", Png);

    private sealed class RefusingMarkup(MailFooterRefusal refusal) : IMailFooterMarkup
    {
        public MailFooterMarkupReading Sanitize(string html, IReadOnlySet<string> imageNames) => new(null, refusal);

        public string ReadPlainText(string html) => html;
    }
}
