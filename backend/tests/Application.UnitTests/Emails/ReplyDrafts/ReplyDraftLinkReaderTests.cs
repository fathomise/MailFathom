// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.EmailContent.Rendering.Document.Blocks;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.ReplyDrafts;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Emails;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.ReplyDrafts;

/// <summary>Covers which addresses a drafting may link to, and in what order it numbers them.</summary>
/// <remarks>
/// A number is all a model may link by, so what this list holds is exactly the set of places a draft can send its
/// reader. What is asserted is that it holds what the person typed and what the correspondence linked, and nothing a
/// host beside the words could not honestly describe.
/// </remarks>
public sealed class ReplyDraftLinkReaderTests
{
    private static readonly StoredEmailId Message = StoredEmailId.Create(Guid.CreateVersion7());

    [Fact]
    public async Task ReadAsync_ALinkTheCorrespondenceCarries_PublishesItsTargetWithTheWordsItWasLinkedFrom()
    {
        // Arrange
        var reader = ReaderOver(LinkedRun("comparison sheet", new MailDocumentLink("https://files.example.test/sheet?token=s", "files.example.test", null, MailLinkDeception.None)));

        // Act
        var links = await reader.ReadAsync(instruction: null, [MessageOf(Message)], TestContext.Current.CancellationToken);

        // Assert
        var link = Assert.Single(links);

        Assert.Equal(0, link.Position);
        Assert.Equal("https://files.example.test/sheet?token=s", link.Link.Target);
        Assert.Equal("comparison sheet", link.Words);
    }

    [Theory]
    [InlineData("https://bank.example.test/login", "bank.example.test", null, MailLinkDeception.DisplayedHostDiffers)]
    [InlineData("https://xn--pple-43d.example.test/", "аpple.example.test", "xn--pple-43d.example.test", MailLinkDeception.None)]
    [InlineData("mailto:anna@example.test", null, null, MailLinkDeception.NotApplicable)]
    public async Task ReadAsync_ALinkAHostCannotHonestlyDescribe_IsLeftOut(
        string target,
        string? host,
        string? asciiHost,
        MailLinkDeception deception)
    {
        // Arrange
        var reader = ReaderOver(LinkedRun("here", new MailDocumentLink(target, host, asciiHost, deception)));

        // Act
        var links = await reader.ReadAsync(instruction: null, [MessageOf(Message)], TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(links);
    }

    [Fact]
    public async Task ReadAsync_AnAddressThePersonTyped_IsNumberedFirstWithNoWordsAndNoTrailingPunctuation()
    {
        // Arrange
        var reader = ReaderOver(LinkedRun("the sheet", new MailDocumentLink("https://files.example.test/sheet", "files.example.test", null, MailLinkDeception.None)));

        // Act
        var links = await reader.ReadAsync(
            "Point them at https://forms.example.test/order. Thanks",
            [MessageOf(Message)],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [("https://forms.example.test/order", (string?)null), ("https://files.example.test/sheet", "the sheet")],
            links.Select(static link => (link.Link.Target, link.Words)));
        Assert.Equal([0, 1], links.Select(static link => link.Position));
    }

    [Theory]
    [InlineData("Link https://wiki.example.test/Mercury_(planet) please", "https://wiki.example.test/Mercury_(planet)")]
    [InlineData("Link it (https://wiki.example.test/Mercury_(planet)).", "https://wiki.example.test/Mercury_(planet)")]
    [InlineData("Link it (https://wiki.example.test/planet).", "https://wiki.example.test/planet")]
    public async Task ReadAsync_AnAddressEndingInABracket_KeepsTheBracketOnlyWhereTheAddressOpensIt(string instruction, string expected)
    {
        // Arrange
        var reader = ReaderOver();

        // Act
        var links = await reader.ReadAsync(instruction, [MessageOf(Message)], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expected, Assert.Single(links).Link.Target);
    }

    [Fact]
    public async Task ReadAsync_AnyMessage_RendersItWithoutInliningItsPictures()
    {
        // Arrange
        var store = Substitute.For<IEmailContentStore>();
        store.FindStoredContentAsync(Message, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StoredEmailContent?>(new StoredEmailContent(new byte[] { 1 }, 1, new byte[32])));
        var renderer = Substitute.For<IEmailContentRenderer>();
        renderer
            .RenderAsync(Arg.Any<StoredEmailContent>(), Arg.Any<EmailContentRenderingBounds>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailContentRenderingResult.Rendered(RenderingOf(MailDocument.Reduced([], 0, 0, 0, 0, truncated: false)))));
        var reader = new ReplyDraftLinkReader(store, renderer);

        // Act
        await reader.ReadAsync(instruction: null, [MessageOf(Message)], TestContext.Current.CancellationToken);

        // Assert
        await renderer.Received(1).RenderAsync(
            Arg.Any<StoredEmailContent>(),
            Arg.Is<EmailContentRenderingBounds>(static bounds => bounds.IncludeMailDocument && bounds.RemainingInlineImageOctetsForRead == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadAsync_OneTargetLinkedTwice_IsPublishedOnce()
    {
        // Arrange
        var link = new MailDocumentLink("https://files.example.test/sheet", "files.example.test", null, MailLinkDeception.None);
        var reader = ReaderOver(LinkedRun("the sheet", link), LinkedRun("this sheet", link));

        // Act
        var links = await reader.ReadAsync(instruction: null, [MessageOf(Message)], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("the sheet", Assert.Single(links).Words);
    }

    [Fact]
    public async Task ReadAsync_MoreDistinctLinksThanTheBound_PublishesTheBoundNumberedInTurnAfterWhatThePersonTyped()
    {
        // Arrange
        var reader = ReaderOver(
        [
            .. Enumerable.Range(0, ReplyDraftLinkReader.MaximumLinks + 5).Select(static index => LinkedRun(
                $"sheet {index}",
                new MailDocumentLink($"https://files.example.test/{index}", "files.example.test", null, MailLinkDeception.None))),
        ]);

        // Act
        var links = await reader.ReadAsync(
            "Point them at https://forms.example.test/order",
            [MessageOf(Message)],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Enumerable.Range(0, ReplyDraftLinkReader.MaximumLinks), links.Select(static link => link.Position));
        Assert.Equal(
            [
                "https://forms.example.test/order",
                .. Enumerable.Range(0, ReplyDraftLinkReader.MaximumLinks - 1).Select(static index => $"https://files.example.test/{index}"),
            ],
            links.Select(static link => link.Link.Target));
    }

    [Fact]
    public async Task ReadAsync_WordsLongerThanTheBound_AreCutToIt()
    {
        // Arrange
        var reader = ReaderOver(LinkedRun(
            new string('w', ReplyDraftLinkReader.MaximumWordsLength + 50),
            new MailDocumentLink("https://files.example.test/sheet", "files.example.test", null, MailLinkDeception.None)));

        // Act
        var links = await reader.ReadAsync(instruction: null, [MessageOf(Message)], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new string('w', ReplyDraftLinkReader.MaximumWordsLength), Assert.Single(links).Words);
    }

    [Fact]
    public async Task ReadAsync_AMessageWithNoStoredContent_PublishesNothingAndRendersNothing()
    {
        // Arrange
        var renderer = Substitute.For<IEmailContentRenderer>();
        var reader = new ReplyDraftLinkReader(Substitute.For<IEmailContentStore>(), renderer);

        // Act
        var links = await reader.ReadAsync(instruction: null, [MessageOf(Message)], TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(links);
        await renderer.DidNotReceive().RenderAsync(Arg.Any<StoredEmailContent>(), Arg.Any<EmailContentRenderingBounds>(), Arg.Any<CancellationToken>());
    }

    private static ReplyDraftLinkReader ReaderOver(params MailParagraphBlock[] blocks)
    {
        var store = Substitute.For<IEmailContentStore>();
        store.FindStoredContentAsync(Message, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StoredEmailContent?>(new StoredEmailContent(new byte[] { 1 }, 1, new byte[32])));

        var renderer = Substitute.For<IEmailContentRenderer>();
        renderer
            .RenderAsync(Arg.Any<StoredEmailContent>(), Arg.Any<EmailContentRenderingBounds>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailContentRenderingResult.Rendered(RenderingOf(
                MailDocument.Reduced(blocks, 0, 0, 0, 0, truncated: false)))));

        return new ReplyDraftLinkReader(store, renderer);
    }

    private static MailParagraphBlock LinkedRun(string words, MailDocumentLink link) =>
        new([new MailInlineRun(words, MailTextEmphasis.None, Foreground: null, link)], MailBlockAlignment.Inherited);

    private static ReplyDraftMessage MessageOf(StoredEmailId storedEmailId) =>
        new(storedEmailId, 0, "Anna", SentAt: null, "See the sheet.", IsChosen: true, Attachments: []);

    private static EmailContentRendering RenderingOf(MailDocument document) => new(
        new EmailContentHeaders("Order", SentAt: null, ReceivedAt: null, [], EmailThreadReferences.None),
        new EmailBodyRepresentation("See the sheet.", 14, EmailBodyTruncation.None),
        SanitizedHtmlBody: null,
        new EmailBodyForms(PlainText: true, Html: true),
        BodyIsEncrypted: false,
        EmailAttachmentSummary.Create(
            [],
            inlineResourceCount: 0,
            isEncrypted: false,
            carriesUnverifiedSignature: false,
            containsUnexpandedTnefPart: false),
        [])
    {
        Document = document,
    };
}
