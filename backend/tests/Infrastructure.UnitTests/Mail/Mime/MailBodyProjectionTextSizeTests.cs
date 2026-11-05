// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.EmailContent.Rendering.Document.Blocks;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Infrastructure.Mail.Mime;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Mime;

/// <summary>Covers how the sizes mail writes reduce onto the closed scale a run carries.</summary>
/// <remarks>
/// The rows are ADR 0047's size table, one row of it per data row here, so the table and this suite are read side by
/// side rather than against each other's wording.
/// </remarks>
public sealed class MailBodyProjectionTextSizeTests
{
    /// <summary>A <c>font-size</c> declaration lands on the step its keyword or its length falls in.</summary>
    [Theory]
    [InlineData("xx-small", MailTextSize.Small)]
    [InlineData("x-small", MailTextSize.Small)]
    [InlineData("small", MailTextSize.Small)]
    [InlineData("12px", MailTextSize.Small)]
    [InlineData("12.49px", MailTextSize.Small)]
    [InlineData("0", MailTextSize.Small)]
    [InlineData("0px", MailTextSize.Small)]
    [InlineData("medium", MailTextSize.Normal)]
    [InlineData("12.5px", MailTextSize.Normal)]
    [InlineData("16px", MailTextSize.Normal)]
    [InlineData("16.99px", MailTextSize.Normal)]
    [InlineData("large", MailTextSize.Large)]
    [InlineData("17px", MailTextSize.Large)]
    [InlineData("20.99px", MailTextSize.Large)]
    [InlineData("x-large", MailTextSize.Larger)]
    [InlineData("21px", MailTextSize.Larger)]
    [InlineData("27.99px", MailTextSize.Larger)]
    [InlineData("xx-large", MailTextSize.Largest)]
    [InlineData("xxx-large", MailTextSize.Largest)]
    [InlineData("28px", MailTextSize.Largest)]
    [InlineData("72px !important", MailTextSize.Largest)]
    public async Task ProduceAsync_FontSizeDeclaration_LandsOnTheStepItFallsIn(string declared, MailTextSize expected)
    {
        // Act
        var document = await DocumentOf($"<p><span style=\"font-size: {declared}\">Words</span></p>");

        // Assert
        Assert.Equal(expected, SingleRunOf(document).Size);
    }

    /// <summary>A point is four thirds of a pixel, and a relative length is read against 16px.</summary>
    [Theory]
    [InlineData("9pt", MailTextSize.Small)]
    [InlineData("10pt", MailTextSize.Normal)]
    [InlineData("13pt", MailTextSize.Large)]
    [InlineData("18pt", MailTextSize.Larger)]
    [InlineData("21pt", MailTextSize.Largest)]
    [InlineData("0.75em", MailTextSize.Small)]
    [InlineData("1em", MailTextSize.Normal)]
    [InlineData("1.125rem", MailTextSize.Large)]
    [InlineData("1.5rem", MailTextSize.Larger)]
    [InlineData("70%", MailTextSize.Small)]
    [InlineData("100%", MailTextSize.Normal)]
    [InlineData("200%", MailTextSize.Largest)]
    public async Task ProduceAsync_PointOrRelativeLength_IsReadAtItsPixelEquivalent(string declared, MailTextSize expected)
    {
        // Act
        var document = await DocumentOf($"<p style=\"font-size:{declared}\">Words</p>");

        // Assert
        Assert.Equal(expected, SingleRunOf(document).Size);
    }

    /// <summary>A legacy <c>font</c> size, absolute or relative to 3, lands on the step the table gives it.</summary>
    [Theory]
    [InlineData("1", MailTextSize.Small)]
    [InlineData("2", MailTextSize.Small)]
    [InlineData("3", MailTextSize.Normal)]
    [InlineData("4", MailTextSize.Large)]
    [InlineData("5", MailTextSize.Larger)]
    [InlineData("6", MailTextSize.Largest)]
    [InlineData("7", MailTextSize.Largest)]
    [InlineData("9", MailTextSize.Largest)]
    [InlineData("+1", MailTextSize.Large)]
    [InlineData("+3", MailTextSize.Largest)]
    [InlineData("-1", MailTextSize.Small)]
    [InlineData("-5", MailTextSize.Small)]
    public async Task ProduceAsync_FontElementSize_LandsOnTheStepTheTableGivesIt(string size, MailTextSize expected)
    {
        // Act
        var document = await DocumentOf($"<p><font size=\"{size}\">Words</font></p>");

        // Assert
        Assert.Equal(expected, SingleRunOf(document).Size);
    }

    /// <summary>A heading's words carry the step its level maps to, while the heading keeps its level.</summary>
    [Theory]
    [InlineData(1, MailTextSize.Largest)]
    [InlineData(2, MailTextSize.Larger)]
    [InlineData(3, MailTextSize.Large)]
    [InlineData(4, MailTextSize.Normal)]
    [InlineData(5, MailTextSize.Normal)]
    [InlineData(6, MailTextSize.Normal)]
    public async Task ProduceAsync_HeadingLevel_GivesItsWordsTheStepTheLevelMapsTo(int level, MailTextSize expected)
    {
        // Act
        var document = await DocumentOf($"<h{level}>Words</h{level}>");

        // Assert
        var heading = Assert.IsType<MailHeadingBlock>(Assert.Single(document.Blocks));
        Assert.Equal(level, heading.Level);
        Assert.Equal(expected, Assert.Single(heading.Content).Size);
    }

    /// <summary>A size the heading's own words were given wins over the step its level maps to.</summary>
    [Fact]
    public async Task ProduceAsync_HeadingWordsSizedByTheMessage_KeepTheSizeTheyWereGiven()
    {
        // Act
        var document = await DocumentOf("<h1 style=\"font-size:13px\">Headline <span style=\"font-size:x-small\">small</span></h1>");

        // Assert
        var heading = Assert.IsType<MailHeadingBlock>(Assert.Single(document.Blocks));
        Assert.Equal([MailTextSize.Normal, MailTextSize.Small], heading.Content.Select(run => run.Size));
    }

    /// <summary>A size is inherited until something inside asks for another, and runs of two sizes are not joined.</summary>
    [Fact]
    public async Task ProduceAsync_NestedSizes_EachRunCarriesTheSizeInForceAndTheyStayApart()
    {
        // Act
        var document = await DocumentOf(
            "<div style=\"font-size:24px\">Lead <b>bold</b> <span style=\"font-size:16px\">body</span></div>");

        // Assert
        var paragraph = Assert.IsType<MailParagraphBlock>(Assert.Single(document.Blocks));
        Assert.Equal(
            [("Lead ", MailTextSize.Larger), ("bold", MailTextSize.Larger), (" ", MailTextSize.Larger), ("body", MailTextSize.Normal)],
            paragraph.Content.Select(run => (run.Text, run.Size)));
    }

    /// <summary>A value that is no size this reads leaves the size in force where it was.</summary>
    [Theory]
    [InlineData("<span style=\"font-size:calc(1px + 2px)\">Words</span>")]
    [InlineData("<span style=\"font-size:-4px\">Words</span>")]
    [InlineData("<span style=\"font-size:1e400px\">Words</span>")]
    [InlineData("<span style=\"font-size:smaller\">Words</span>")]
    [InlineData("<font size=\"big\">Words</font>")]
    [InlineData("<span size=\"1\">Words</span>")]
    public async Task ProduceAsync_SizeThatIsNoneThisReads_LeavesTheInheritedSize(string inner)
    {
        // Act
        var document = await DocumentOf($"<p style=\"font-size:18px\">{inner}</p>");

        // Assert
        Assert.Equal(MailTextSize.Large, SingleRunOf(document).Size);
    }

    /// <summary>A message that sizes nothing carries every run at body size.</summary>
    [Fact]
    public async Task ProduceAsync_MessageSizingNothing_CarriesEveryRunAtBodySize()
    {
        // Act
        var document = await DocumentOf("<p>Plain words<br>on two lines</p>");

        // Assert
        var paragraph = Assert.IsType<MailParagraphBlock>(Assert.Single(document.Blocks));
        Assert.All(paragraph.Content, run => Assert.Equal(MailTextSize.Normal, run.Size));
    }

    private static MailInlineRun SingleRunOf(MailDocument document) =>
        Assert.Single(Assert.IsType<MailParagraphBlock>(Assert.Single(document.Blocks)).Content);

    private static async Task<MailDocument> DocumentOf(string markup)
    {
        var renderer = new MimeKitEmailContentRenderer(new EmailMimeExtractionOptions { MaxPartCount = 1000 });

        var result = await renderer.RenderAsync(
            MimeFixtures.StoredMessage(
                "From: sender@example.test",
                "Content-Type: text/html; charset=utf-8",
                string.Empty,
                markup),
            new EmailContentRenderingBounds(false, 100_000, int.MaxValue) { IncludeMailDocument = true },
            TestContext.Current.CancellationToken);

        Assert.Equal(EmailContentRenderingOutcome.Rendered, result.Outcome);

        var document = result.Rendering!.Document;
        Assert.NotNull(document);

        return document;
    }
}
