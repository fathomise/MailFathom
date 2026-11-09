// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.EmailContent.Rendering.Document.Blocks;
using MailFathom.Application.Mail.Delivery.Footers;
using MailFathom.Infrastructure.Mail.Mime.Composition;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using MimeKit;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Mime;

/// <summary>Covers how the document names the footer MailFathom appended, read off the very message the composer footed.</summary>
/// <remarks>
/// Each message is footed by <see cref="MimeKitMailFooterComposer" /> rather than written out by hand, so what is
/// asserted is that the reading finds the mark the writing leaves, and a change to either shows up here.
/// </remarks>
public sealed class MailBodyProjectionFooterTests
{
    private const string Attribution = "On 2026-08-17 09:30 UTC, bob@example.test wrote:";

    private static readonly MailFooter Footer = new(
        MailFooterSource.Own,
        "<p>Anna Kowalska</p><p>+48 600 000 000</p>",
        "Anna Kowalska\n\n+48 600 000 000",
        []);

    private readonly MimeKitMailFooterComposer composer = new();

    [Fact]
    public async Task ProduceAsync_TheReadersOwnFootedMessage_NamesTheFootersBlocksAndKeepsThem()
    {
        // Arrange
        var footed = this.composer.Append(Composed("Dzień dobry.", "<p>Dzień dobry.</p>"), Footer);

        // Act
        var document = await MimeFixtures.DocumentOf(footed, authorIsProven: true);

        // Assert
        Assert.Equal(new MailDocumentFooter(1, 2), document.Footer);
        Assert.Equal(["Dzień dobry.", "Anna Kowalska", "+48 600 000 000"], ParagraphTexts(document));
    }

    [Fact]
    public async Task ProduceAsync_TheReadersOwnFootedReply_NamesTheFooterAboveTheQuotationAndStillReducesTheQuotation()
    {
        // Arrange
        var html = $"<p>Thank you.</p>{IMailFooterComposer.HtmlQuotationMarker}<p>{Attribution}</p><blockquote><p>The report.</p><footer><p>Bob</p></footer></blockquote>";
        var footed = this.composer.Append(Composed($"Thank you.\n\n{Attribution}\n> The report.", html, answers: true), Footer);

        // Act
        var document = await MimeFixtures.DocumentOf(footed, authorIsProven: true);

        // Assert
        Assert.Equal(new MailDocumentFooter(1, 2), document.Footer);
        var quote = Assert.IsType<MailQuoteBlock>(document.Blocks[^1]);
        Assert.Equal(2, quote.Blocks.Count);
    }

    [Fact]
    public async Task ProduceAsync_AFooterTheQuotationAloneCarries_NamesNone()
    {
        // Arrange
        var html = $"<p>Thank you.</p>{IMailFooterComposer.HtmlQuotationMarker}<p>{Attribution}</p><blockquote><p>The report.</p><footer><p>Bob</p></footer></blockquote>";

        // Act
        var document = await MimeFixtures.DocumentOf(Composed("Thank you.", html, answers: true), authorIsProven: true);

        // Assert
        Assert.Null(document.Footer);
    }

    [Fact]
    public async Task ProduceAsync_AFootedMessageNotTheReadersOwn_NamesNoFooterAndDrawsTheSameBlocks()
    {
        // Arrange
        var footed = this.composer.Append(Composed("Dzień dobry.", "<p>Dzień dobry.</p>"), Footer);

        // Act
        var own = await MimeFixtures.DocumentOf(footed, authorIsProven: true);
        var foreign = await MimeFixtures.DocumentOf(footed, authorIsProven: false);

        // Assert
        Assert.Null(foreign.Footer);
        Assert.Equal(own.Blocks.Count, foreign.Blocks.Count);
        Assert.Equal(ParagraphTexts(own), ParagraphTexts(foreign));
    }

    [Theory]
    [InlineData("<p>Dzień dobry.</p>")]
    [InlineData(null)]
    public async Task ProduceAsync_TheReadersOwnMessageCarryingNoMark_NamesNoFooter(string? html)
    {
        // Act
        var document = await MimeFixtures.DocumentOf(Composed("Dzień dobry.\n\nAnna", html), authorIsProven: true);

        // Assert
        Assert.Null(document.Footer);
    }

    [Fact]
    public async Task ProduceAsync_TheReadersOwnFootedTextOnlyMessage_NamesTheFooterFromItsSignatureLine()
    {
        // Arrange
        var footed = this.composer.Append(Composed("Dzień dobry.", html: null), Footer);

        // Act
        var document = await MimeFixtures.DocumentOf(footed, authorIsProven: true);

        // Assert
        Assert.Equal(new MailDocumentFooter(1, 2), document.Footer);
        Assert.Equal(["Dzień dobry.", "-- \nAnna Kowalska", "+48 600 000 000"], ParagraphTexts(document));
    }

    [Fact]
    public async Task ProduceAsync_ASignatureTheAuthorTypedInsideAParagraph_NamesNoFooterAndDrawsTheSameBlocks()
    {
        // Arrange
        var typed = Composed("Hello.\n-- \nAnna", html: null);

        // Act
        var own = await MimeFixtures.DocumentOf(typed, authorIsProven: true);
        var foreign = await MimeFixtures.DocumentOf(typed, authorIsProven: false);

        // Assert
        Assert.Null(own.Footer);
        Assert.Equal(ParagraphTexts(foreign), ParagraphTexts(own));
    }

    [Fact]
    public async Task ProduceAsync_TheReadersOwnFootedTextOnlyReply_NamesTheFooterAboveTheQuotation()
    {
        // Arrange
        var text = $"Thank you.\n\n{Attribution}\n> The report.\n> -- \n> Bob";
        var footed = this.composer.Append(Composed(text, html: null, answers: true), Footer);

        // Act
        var document = await MimeFixtures.DocumentOf(footed, authorIsProven: true);

        // Assert
        Assert.Equal(new MailDocumentFooter(1, 2), document.Footer);
        Assert.StartsWith(Attribution, ParagraphTexts(document)[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProduceAsync_AProvenReply_NamesTheQuotationFromItsAttributionToTheEndAndSaysTheAuthorIsProven()
    {
        // Arrange
        var html = $"<p>Thank you.</p>{IMailFooterComposer.HtmlQuotationMarker}<p>{Attribution}</p><blockquote><p>The report.</p></blockquote>";
        var footed = this.composer.Append(Composed($"Thank you.\n\n{Attribution}\n> The report.", html, answers: true), Footer);

        // Act
        var document = await MimeFixtures.DocumentOf(footed, authorIsProven: true);

        // Assert
        Assert.True(document.AuthorIsProven);
        Assert.Equal(new MailDocumentFooter(1, 2), document.Footer);
        Assert.Equal(new MailDocumentQuotation(3, 2), document.Quotation);
        Assert.Equal(Attribution, ParagraphTexts(document)[^1]);
        Assert.IsType<MailQuoteBlock>(document.Blocks[^1]);
    }

    [Fact]
    public async Task ProduceAsync_ABodyOfWordsAndTheMarkWithNoParagraphAroundThem_NamesTheQuotationAfterTheWords()
    {
        // Arrange
        var html = $"Thank you.{IMailFooterComposer.HtmlQuotationMarker}<p>{Attribution}</p><blockquote><p>The report.</p></blockquote>";

        // Act
        var document = await MimeFixtures.DocumentOf(Composed("Thank you.", html, answers: true), authorIsProven: true);

        // Assert
        Assert.Equal(new MailDocumentQuotation(1, 2), document.Quotation);
    }

    [Fact]
    public async Task ProduceAsync_AMarkNestedBelowTheBody_NamesNoQuotation()
    {
        // Arrange
        var html = $"<div><p>Thank you.</p>{IMailFooterComposer.HtmlQuotationMarker}<p>{Attribution}</p></div>";

        // Act
        var document = await MimeFixtures.DocumentOf(Composed("Thank you.", html), authorIsProven: true);

        // Assert
        Assert.Null(document.Quotation);
    }

    [Theory]
    [InlineData("<p>Thank you.</p><blockquote><p>The report.</p></blockquote>")]
    [InlineData(null)]
    public async Task ProduceAsync_AProvenMessageCarryingNoQuotationMark_NamesNoQuotation(string? html)
    {
        // Act
        var document = await MimeFixtures.DocumentOf(Composed("Thank you.", html), authorIsProven: true);

        // Assert
        Assert.True(document.AuthorIsProven);
        Assert.Null(document.Quotation);
    }

    [Fact]
    public async Task ProduceAsync_AMarkedMessageNothingProves_ProducesTheSameDocumentAsBeforeTheMarksWereRead()
    {
        // Arrange
        var html = $"<p style=\"color:#db0118\">Thank you.</p>{IMailFooterComposer.HtmlQuotationMarker}<p>{Attribution}</p><blockquote><p>The report.</p></blockquote>";
        var footed = this.composer.Append(Composed("Thank you.", html, answers: true), Footer);

        // Act
        var proven = await MimeFixtures.DocumentOf(footed, authorIsProven: true);
        var unproven = await MimeFixtures.DocumentOf(footed, authorIsProven: false);

        // Assert
        Assert.False(unproven.AuthorIsProven);
        Assert.Null(unproven.Footer);
        Assert.Null(unproven.Quotation);
        Assert.Equal(proven.Blocks.Count, unproven.Blocks.Count);
        Assert.Equal(ParagraphTexts(proven), ParagraphTexts(unproven));
    }

    [Fact]
    public async Task RenderAsync_TheReadersOwnFootedMessage_LeavesTheSendersOwnMarkupAsItWas()
    {
        // Arrange
        var footed = this.composer.Append(Composed("Dzień dobry.", "<p>Dzień dobry.</p>"), Footer);

        // Act
        var own = await MimeFixtures.RenderingOf(footed, authorIsProven: true);
        var foreign = await MimeFixtures.RenderingOf(footed, authorIsProven: false);

        // Assert
        Assert.NotNull(own.SelfContainedHtmlBody);
        Assert.Equal(foreign.SelfContainedHtmlBody, own.SelfContainedHtmlBody);
    }

    private static string[] ParagraphTexts(MailDocument document) =>
    [
        .. document.Blocks.OfType<MailParagraphBlock>().Select(paragraph => string.Concat(paragraph.Content.Select(run => run.Text))),
    ];

    private static byte[] Composed(string text, string? html, bool answers = false)
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
            MessageId = "composed-1@example.test",
            Body = body,
        };
        message.From.Add(new MailboxAddress("Anna Kowalska", "anna@example.test"));
        message.To.Add(new MailboxAddress("Bob", "bob@example.test"));

        if (answers)
        {
            message.InReplyTo = "original@example.test";
        }

        using var written = new MemoryStream();
        message.WriteTo(written);

        return written.ToArray();
    }
}
