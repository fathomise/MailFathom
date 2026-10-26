// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Spam.Model;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Mail.Mime;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Mime;

/// <summary>Covers what the spam model stage reads out of a message through the renderer a deployment runs.</summary>
/// <remarks>
/// The reader is held against the real renderer here because the defect these guard against lives between the two: the
/// renderer cuts the markup to whatever budget it is handed before it finds a single anchor, so a reader sharing the
/// body's budget with the links passed its own tests over a stub and lost every link of long or heavily styled mail.
/// </remarks>
public sealed class SpamModelEvidenceRenderingTests
{
    private const int BodyBound = 3_000;

    private static readonly StoredEmailId Email = StoredEmailId.Create(Guid.Parse("0199a0c0-0000-7000-8000-0000000051a1"));

    [Fact]
    public async Task ReadAsync_ABodyAtTheBoundAndALinkBehindALongStylesheet_CutsTheBodyAndStillReadsTheLink()
    {
        // Arrange
        var plainText = string.Join(' ', Enumerable.Repeat("offer", BodyBound));
        var stylesheet = string.Concat(Enumerable.Range(0, 2_000).Select(index => $".rule{index}{{color:#123456}}"));
        var content = MimeFixtures.StoredMessage(
            "From: promotions@example.net",
            "Subject: Your account",
            "MIME-Version: 1.0",
            "Content-Type: multipart/alternative; boundary=\"part\"",
            string.Empty,
            "--part",
            "Content-Type: text/plain; charset=utf-8",
            string.Empty,
            plainText,
            "--part",
            "Content-Type: text/html; charset=utf-8",
            string.Empty,
            $"<html><head><style>{stylesheet}</style></head><body><p>{plainText}</p>"
                + "<p><a href=\"https://login.example.net/verify\">https://bank.example.com</a></p></body></html>",
            "--part--");

        // Act
        var evidence = await Reader().ReadAsync(Email, content, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(evidence);
        Assert.True(evidence.IsBodyCut);
        Assert.True(evidence.Body.Length <= BodyBound);
        Assert.Equal(new SpamModelLink("login.example.net", "bank.example.com", DisplayedHostDiffers: true), Assert.Single(evidence.Links));
    }

    [Fact]
    public async Task ReadAsync_APlainTextMessageWithALinkPastTheBodysBound_StillReadsTheLink()
    {
        // Arrange
        var content = MimeFixtures.StoredMessage(
            "From: promotions@example.net",
            "Subject: Your parcel",
            "Content-Type: text/plain; charset=utf-8",
            string.Empty,
            string.Join("\r\n\r\n", Enumerable.Repeat(string.Join(' ', Enumerable.Repeat("parcel", 50)), 60)),
            string.Empty,
            "https://track.example.org/parcel");

        // Act
        var evidence = await Reader().ReadAsync(Email, content, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(evidence);
        Assert.True(evidence.IsBodyCut);
        Assert.DoesNotContain("track.example.org", evidence.Body, StringComparison.Ordinal);
        Assert.Contains(evidence.Links, link => link.TargetHost == "track.example.org");
    }

    private static SpamModelEvidenceReader Reader() => new(
        new MimeKitEmailContentRenderer(new EmailMimeExtractionOptions()),
        StubEmailProvenanceReader.AllKnownCorrespondents,
        new SpamModelStageBounds(SpamModelStageBounds.DefaultMaximumConcurrentCalls, BodyBound));
}
