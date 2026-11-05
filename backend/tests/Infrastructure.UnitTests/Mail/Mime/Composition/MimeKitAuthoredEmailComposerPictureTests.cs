// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Rendering.Document.Blocks;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Footers;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Infrastructure.Mail.Mime;
using MailFathom.Infrastructure.Mail.Mime.Composition;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using Microsoft.Extensions.Time.Testing;
using MimeKit;
using NSubstitute;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Mime.Composition;

/// <summary>
/// Covers the pictures an author places inside a message: the <c>multipart/related</c> they travel in, the content
/// identifier the composition mints in place of the name the author's composer chose, every refusal that stops a
/// picture from being sent, and the round trip a saved draft makes back into the document a composer reopens.
/// </summary>
public sealed class MimeKitAuthoredEmailComposerPictureTests
{
    private const string ChartName = "0198f0a0-4444-7000-8000-000000000001";

    private const string LogoName = "0198f0a0-4444-7000-8000-000000000002";

    private static readonly MailAccountId Account = MailAccountId.Create("primary");

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03];

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];

    /// <summary>The markup and its picture become a <c>multipart/related</c> inside the alternative beside the plain text.</summary>
    [Fact]
    public void ComposeDraft_AMessageCarryingAPicture_PutsItBesideTheMarkupInsideTheAlternative()
    {
        // Act
        using var message = ParseDraft(CreateComposer().ComposeDraft(Account, WithChart(), Capabilities()));

        // Assert
        var alternative = Assert.IsType<MultipartAlternative>(message.Body);
        Assert.True(Assert.IsType<TextPart>(alternative[0]).IsPlain);
        var related = Assert.IsType<MultipartRelated>(alternative[1]);
        Assert.True(Assert.IsType<TextPart>(related[0]).IsHtml);
        var picture = Assert.IsType<MimePart>(related[1]);
        Assert.Equal("image/png", picture.ContentType.MimeType);
        Assert.Equal(Png, ReadContent(picture));
        Assert.Equal(2, related.Count);
    }

    /// <summary>An attachment beside a picture puts the alternative inside a <c>multipart/mixed</c>, and the picture stays inside the alternative.</summary>
    [Fact]
    public void ComposeDraft_APictureBesideAnAttachment_KeepsThePictureInsideTheAlternative()
    {
        // Arrange
        var authored = WithChart() with
        {
            Attachments = [new AuthoredEmailAttachment("notes.txt", "text/plain", "notes"u8.ToArray())],
        };

        // Act
        using var message = ParseDraft(CreateComposer().ComposeDraft(Account, authored, Capabilities()));

        // Assert
        var mixed = Assert.IsType<Multipart>(message.Body);
        Assert.Equal("mixed", mixed.ContentType.MediaSubtype);
        var alternative = Assert.IsType<MultipartAlternative>(mixed[0]);
        Assert.IsType<MultipartRelated>(alternative[1]);
        Assert.Equal("notes.txt", Assert.IsType<TextPart>(mixed[1]).FileName);
    }

    /// <summary>
    /// The part carries a content identifier this composition minted under the account's domain, the markup refers to
    /// that identifier, and the name the author's composer chose appears nowhere in what is sent.
    /// </summary>
    [Fact]
    public void ComposeDraft_APicture_IsReferredToByAContentIdentifierTheCompositionMinted()
    {
        // Act
        var composition = CreateComposer().ComposeDraft(Account, WithChart(), Capabilities());

        // Assert
        using var message = ParseDraft(composition);
        var picture = Assert.IsType<MimePart>(Assert.IsType<MultipartRelated>(Assert.IsType<MultipartAlternative>(message.Body)[1])[1]);
        Assert.EndsWith("@example.test", picture.ContentId, StringComparison.Ordinal);
        Assert.Contains($"<img src=\"cid:{picture.ContentId}\">", message.HtmlBody, StringComparison.Ordinal);
        Assert.Null(picture.FileName);
        Assert.DoesNotContain(ChartName, Encoding.ASCII.GetString(composition.Draft!.RawMime.Span), StringComparison.Ordinal);
    }

    /// <summary>What a picture is sent as is read from its octets, not from anything the author declared.</summary>
    [Fact]
    public void ComposeDraft_APictureOpeningAsAJpeg_IsSentAsAJpeg()
    {
        // Arrange
        var authored = WithChart() with { Pictures = [new AuthoredEmailPicture(ChartName, Jpeg)] };

        // Act
        using var message = ParseDraft(CreateComposer().ComposeDraft(Account, authored, Capabilities()));

        // Assert
        var related = Assert.IsType<MultipartRelated>(Assert.IsType<MultipartAlternative>(message.Body)[1]);
        Assert.Equal("image/jpeg", Assert.IsType<MimePart>(related[1]).ContentType.MimeType);
    }

    /// <summary>More pictures than one message carries are refused on the count, before any name or octet is looked at.</summary>
    [Fact]
    public void ComposeDraft_MorePicturesThanAMessageCarries_IsRefusedOnTheCount()
    {
        // Arrange
        var authored = WithChart() with
        {
            Pictures = [.. Enumerable.Range(0, AuthoredEmailPicture.MaximumCount + 1)
                .Select(_ => new AuthoredEmailPicture("not a name", "not a picture"u8.ToArray()))],
        };

        // Act
        var composition = CreateComposer().ComposeDraft(Account, authored, Capabilities());

        // Assert
        AssertRefused(composition, AuthoredEmailRefusalReason.TooManyPictures, MailFathomErrorCode.AuthoredMailPicturesTooMany);
        Assert.Equal(AuthoredEmailPicture.MaximumCount, composition.Refusal!.Bound);
    }

    /// <summary>A name the composer never mints and a name two pictures share are both refused, since a name is what the markup refers to.</summary>
    /// <param name="secondName">The name the second picture carries.</param>
    [Theory]
    [InlineData("not a name")]
    [InlineData(ChartName)]
    public void ComposeDraft_APictureNameNoReferenceCouldSingleOut_IsRefused(string secondName)
    {
        // Arrange
        var authored = WithChart() with
        {
            HtmlBody = $"<p>Two</p><img src=\"cid:{ChartName}\"><img src=\"cid:{secondName}\">",
            Pictures = [new AuthoredEmailPicture(ChartName, Png), new AuthoredEmailPicture(secondName, Png)],
        };

        // Act
        var composition = CreateComposer().ComposeDraft(Account, authored, Capabilities());

        // Assert
        AssertRefused(composition, AuthoredEmailRefusalReason.PictureNameUnusable, MailFathomErrorCode.AuthoredMailPictureNameRefused);
    }

    /// <summary>One picture past what a reopened draft draws inline is refused, naming that bound.</summary>
    [Fact]
    public void ComposeDraft_APictureLargerThanAReopenedDraftDraws_IsRefused()
    {
        // Arrange
        var authored = WithChart() with { Pictures = [Picture(ChartName, AuthoredEmailPicture.MaximumOctets + 1)] };

        // Act
        var composition = CreateComposer().ComposeDraft(Account, authored, Capabilities());

        // Assert
        AssertRefused(composition, AuthoredEmailRefusalReason.PictureTooLarge, MailFathomErrorCode.AuthoredMailPictureTooLarge);
        Assert.Equal(AuthoredEmailPicture.MaximumOctets, composition.Refusal!.Bound);
    }

    /// <summary>Pictures each within their own bound but past one message's together are refused, naming the bound on the sum.</summary>
    [Fact]
    public void ComposeDraft_PicturesTogetherLargerThanAMessageCarries_IsRefused()
    {
        // Arrange
        var half = (AuthoredEmailPicture.MaximumTotalOctets / 2) + 1;
        var authored = WithChart() with
        {
            HtmlBody = $"<img src=\"cid:{ChartName}\"><img src=\"cid:{LogoName}\">",
            Pictures = [Picture(ChartName, half), Picture(LogoName, half)],
        };

        // Act
        var composition = CreateComposer().ComposeDraft(Account, authored, Capabilities());

        // Assert
        AssertRefused(composition, AuthoredEmailRefusalReason.PicturesTooLarge, MailFathomErrorCode.AuthoredMailPicturesTooLarge);
        Assert.Equal(AuthoredEmailPicture.MaximumTotalOctets, composition.Refusal!.Bound);
    }

    /// <summary>Octets opening as none of PNG, JPEG, and GIF are refused, whatever they are.</summary>
    [Fact]
    public void ComposeDraft_APictureOpeningAsNoAcceptedType_IsRefused()
    {
        // Arrange
        var authored = WithChart() with { Pictures = [new AuthoredEmailPicture(ChartName, "<svg/>"u8.ToArray())] };

        // Act
        var composition = CreateComposer().ComposeDraft(Account, authored, Capabilities());

        // Assert
        AssertRefused(composition, AuthoredEmailRefusalReason.PictureTypeNotAccepted, MailFathomErrorCode.AuthoredMailPictureTypeRefused);
    }

    /// <summary>A reference naming a picture the message does not carry is refused, with or without pictures beside it.</summary>
    /// <param name="carriesAPicture">Whether the message carries a picture the reference does not name.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComposeDraft_AReferenceNamingNoPicture_IsRefused(bool carriesAPicture)
    {
        // Arrange
        var authored = WithChart() with
        {
            HtmlBody = $"<img src=\"cid:{ChartName}\"><img src=\"cid:{LogoName}\">",
            Pictures = carriesAPicture ? [new AuthoredEmailPicture(ChartName, Png)] : [],
        };

        // Act
        var composition = CreateComposer().ComposeDraft(Account, authored, Capabilities());

        // Assert
        AssertRefused(composition, AuthoredEmailRefusalReason.PictureReferenceUnknown, MailFathomErrorCode.AuthoredMailPictureReferenceUnknown);
    }

    /// <summary>A picture the markup never draws is refused rather than sent where nobody would see it.</summary>
    /// <param name="htmlBody">The markup the message carries beside the picture.</param>
    [Theory]
    [InlineData("<p>No picture here.</p>")]
    [InlineData(null)]
    public void ComposeDraft_APictureNoReferenceNames_IsRefused(string? htmlBody)
    {
        // Arrange
        var authored = WithChart() with { HtmlBody = htmlBody };

        // Act
        var composition = CreateComposer().ComposeDraft(Account, authored, Capabilities());

        // Assert
        AssertRefused(composition, AuthoredEmailRefusalReason.PictureUnreferenced, MailFathomErrorCode.AuthoredMailPictureUnreferenced);
    }

    /// <summary>The pictures count against the whole message's bound like every other part of it.</summary>
    [Fact]
    public void ComposeDraft_APictureTakingTheMessagePastItsBound_IsRefusedAsTheWholeMessage()
    {
        // Arrange
        var composer = CreateComposer(maxMessageBytes: 4096);
        var authored = WithChart() with { Pictures = [Picture(ChartName, 4000)] };

        // Act
        var composition = composer.ComposeDraft(Account, authored, Capabilities());

        // Assert
        Assert.Equal(AuthoredEmailRefusalReason.BoundExceeded, composition.Refusal!.Reason);
        Assert.Equal(AuthoredEmailField.Message, composition.Refusal.Field);
    }

    /// <summary>A draft saved with a picture reopens with it, inlined into the document as the octets it was saved with.</summary>
    [Fact]
    public async Task ComposeDraft_ADraftSavedWithAPicture_ReopensWithItThroughTheReducedDocument()
    {
        // Arrange
        var composition = CreateComposer().ComposeDraft(Account, WithChart(), Capabilities());
        var renderer = new MimeKitEmailContentRenderer(new EmailMimeExtractionOptions { MaxPartCount = 1000 });

        // Act
        var result = await renderer.RenderAsync(
            MimeFixtures.StoredRawContent(composition.Draft!.RawMime.ToArray()),
            new EmailContentRenderingBounds(false, 100_000, int.MaxValue) { IncludeMailDocument = true },
            TestContext.Current.CancellationToken);

        // Assert
        var document = result.Rendering!.Document!;
        var image = Assert.Single(document.Blocks.OfType<MailImageBlock>()).Image;
        Assert.Equal($"data:image/png;base64,{Convert.ToBase64String(Png)}", image.Source);
    }

    /// <summary>A footer appended at send joins the pictures' <c>multipart/related</c> rather than opening a second one.</summary>
    [Fact]
    public void ComposeDraft_AFooterAppendedToAMessageCarryingAPicture_SharesItsRelatedPart()
    {
        // Arrange
        var composition = CreateComposer().ComposeDraft(Account, WithChart(), Capabilities());
        var footer = new MailFooter(
            MailFooterSource.Own,
            "<p>Anna</p><img src=\"cid:logo.png\">",
            "Anna",
            [new MailFooterImage("logo.png", "image/png", Png)]);

        // Act
        var footed = new MimeKitMailFooterComposer().Append(composition.Draft!.RawMime, footer);

        // Assert
        using var stream = new MemoryStream(footed.ToArray());
        using var message = MimeMessage.Load(stream, TestContext.Current.CancellationToken);
        var related = Assert.IsType<MultipartRelated>(Assert.IsType<MultipartAlternative>(message.Body)[1]);
        var pictures = related.OfType<MimePart>().Where(static part => part is not TextPart).ToArray();
        Assert.Equal(2, pictures.Length);
        Assert.Equal(2, pictures.Select(static picture => picture.ContentId).Distinct(StringComparer.Ordinal).Count());
    }

    private static void AssertRefused(
        MailDraftComposition composition,
        AuthoredEmailRefusalReason reason,
        MailFathomErrorCode published)
    {
        Assert.False(composition.IsComposed);
        Assert.Equal(reason, composition.Refusal!.Reason);
        Assert.Equal(AuthoredEmailField.Picture, composition.Refusal.Field);
        Assert.Equal(published, MailDraftRefusedException.From(composition.Refusal).ErrorCode);
    }

    private static MimeMessage ParseDraft(MailDraftComposition composition)
    {
        Assert.True(composition.IsComposed);

        using var stream = new MemoryStream(composition.Draft!.RawMime.ToArray());

        return MimeMessage.Load(stream);
    }

    private static byte[] ReadContent(MimePart part)
    {
        Assert.NotNull(part.Content);
        using var decoded = new MemoryStream();
        part.Content.DecodeTo(decoded);

        return decoded.ToArray();
    }

    private static AuthoredEmailPicture Picture(string name, int octets)
    {
        var content = new byte[octets];
        Png.CopyTo(content, 0);

        return new AuthoredEmailPicture(name, content);
    }

    private static AuthoredEmail WithChart() => new()
    {
        Recipients = [new AuthoredEmailRecipient(OutgoingRecipientRole.To, "anna@example.test", "Anna")],
        Subject = "Quarterly chart",
        PlainTextBody = "See the chart.",
        HtmlBody = $"<p>See the chart.</p><img src=\"cid:{ChartName}\">",
        Pictures = [new AuthoredEmailPicture(ChartName, Png)],
    };

    private static MimeKitAuthoredEmailComposer CreateComposer(long maxMessageBytes = 16 * 1024 * 1024)
    {
        var senderIdentities = Substitute.For<IOutgoingSenderIdentityReader>();
        Assert.True(EmailAddress.TryCreate("MailFathom", "mailfathom@example.test", out var address));
        senderIdentities.FindSenderIdentity(Account).Returns(OutgoingSenderIdentity.Create(Account, address));

        return new MimeKitAuthoredEmailComposer(
            senderIdentities,
            new OutgoingEmailBounds
            {
                MaxRecipientCount = 3,
                MaxBodyCharacters = 4096,
                MaxAttachmentCount = 2,
                MaxAttachmentBytes = 128,
                MaxMessageBytes = maxMessageBytes,
            },
            new FakeTimeProvider(new DateTimeOffset(2026, 11, 5, 9, 30, 0, TimeSpan.Zero)));
    }

    private static MailDeliveryCapabilities Capabilities() => new(null, true, true);
}
