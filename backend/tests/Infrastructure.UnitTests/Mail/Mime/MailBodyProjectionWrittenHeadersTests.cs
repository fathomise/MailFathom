// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Net;
using System.Text;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.EmailContent.Rendering.Document.Blocks;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using MimeKit;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Mime;

/// <summary>Covers how the document names a block of header lines another mail client typed into a message.</summary>
public sealed class MailBodyProjectionWrittenHeadersTests
{
    private const string Author = "Ewa Zielińska";

    private const string ForwardedWords = "Our forecast is 4.1%.";

    public static TheoryData<string, string> BlocksClientsWrite => new()
    {
        { "---------- Forwarded message ---------\nFrom: Ewa Zielińska <ewa@example.test>\nDate: Mon, 24 Aug 2026 at 16:02\nSubject: CPI\nTo: Marta <marta@example.test>", "Mon, 24 Aug 2026 at 16:02" },
        { "---------- Forwarded message ---------\nOd: Ewa Zielińska <ewa@example.test>\nDate: pon., 2 lis 2026 o 16:02\nSubject: CPI\nTo: Marta <marta@example.test>", "pon., 2 lis 2026 o 16:02" },
        { "---------- Weitergeleitete Nachricht ---------\nVon: Ewa Zielińska <ewa@example.test>\nDate: Mo., 24. Aug. 2026 um 16:02 Uhr\nSubject: CPI\nTo: Marta <marta@example.test>", "Mo., 24. Aug. 2026 um 16:02 Uhr" },
        { "-----Original Message-----\nFrom: Ewa Zielińska [mailto:ewa@example.test]\nSent: Monday, August 24, 2026 4:02 PM\nTo: Marta\nCc: Piotr\nSubject: CPI", "Monday, August 24, 2026 4:02 PM" },
        { "-----Wiadomość oryginalna-----\nOd: Ewa Zielińska\nWysłano: poniedziałek, 24 sierpnia 2026 16:02\nDo: Marta\nDW: Piotr\nTemat: CPI", "poniedziałek, 24 sierpnia 2026 16:02" },
        { "-----Ursprüngliche Nachricht-----\nVon: Ewa Zielińska\nGesendet: Montag, 24. August 2026 16:02\nAn: Marta\nCc: Piotr\nBetreff: CPI", "Montag, 24. August 2026 16:02" },
        { "From: Ewa Zielińska <ewa@example.test>\nSubject: CPI\nDate: August 24, 2026 at 4:02:00 PM GMT+2\nTo: Marta", "August 24, 2026 at 4:02:00 PM GMT+2" },
        { "Od: \"Ewa Zielińska\" <ewa@example.test>\nTemat: CPI\nData: 24 sierpnia 2026 16:02:00 CEST\nDo: Marta", "24 sierpnia 2026 16:02:00 CEST" },
        { "Von: Ewa Zielińska <ewa@example.test>\nBetreff: CPI\nDatum: 24. August 2026 um 16:02:00 MESZ\nAn: Marta", "24. August 2026 um 16:02:00 MESZ" },
    };

    public static TheoryData<string> RunsNotRecognized => new()
    {
        "From: Ewa\nX-Mailer: Something\nDate: today\nTo: Marta",
        "From: Ewa\nDate: today\nTo: Marta\nFiled-Under: Finance",
        $"From: Ewa\nDate: today\nTo: {new string('m', 1100)}",
        "Date: today\nTo: Marta\nSubject: CPI",
        "From: Ewa\nTo: Marta\nSubject: CPI",
        "From: Ewa\nDate: today",
    };

    [Theory]
    [MemberData(nameof(BlocksClientsWrite))]
    public async Task ProduceAsync_APlainTextBlockAClientWrote_NamesItWithTheAuthorAndTheDateAsWritten(
        string block,
        string date)
    {
        // Arrange
        var message = Composed($"{block}\n\n{ForwardedWords}", html: null);

        // Act
        var document = await MimeFixtures.DocumentOf(message);

        // Assert
        Assert.Equal(new MailWrittenHeaders(1, Author, date), document.Blocks[0].WrittenHeaders);
        Assert.Null(document.Blocks[1].WrittenHeaders);
        Assert.Equal(ForwardedWords, TextOf(document.Blocks[1]));
        Assert.Equal(block, TextOf(document.Blocks[0]));
    }

    [Theory]
    [MemberData(nameof(BlocksClientsWrite))]
    public async Task ProduceAsync_AnHtmlBlockAClientWrote_NamesItWithTheAuthorAndTheDateAsWritten(
        string block,
        string date)
    {
        // Arrange
        var markup = WebUtility.HtmlEncode(block).Replace("\n", "<br>", StringComparison.Ordinal);
        var message = Composed(block, $"<div>{markup}<br></div><div>{ForwardedWords}</div>");

        // Act
        var document = await MimeFixtures.DocumentOf(message);

        // Assert
        Assert.Equal(new MailWrittenHeaders(1, Author, date), document.Blocks[0].WrittenHeaders);
        Assert.Equal(ForwardedWords, TextOf(document.Blocks[1]));
    }

    [Fact]
    public async Task ProduceAsync_LinesEachInTheirOwnParagraphUnderARule_NamesEveryBlockOfTheRun()
    {
        // Arrange
        var html = "<hr><p>From: Ewa Zielińska</p><p>Date: today</p><p>To: Marta</p><p>Subject: CPI</p><p>" + ForwardedWords + "</p>";

        // Act
        var document = await MimeFixtures.DocumentOf(Composed("x", html));

        // Assert
        Assert.IsType<MailSeparatorBlock>(document.Blocks[0]);
        Assert.Equal(new MailWrittenHeaders(5, Author, "today"), document.Blocks[0].WrittenHeaders);
        Assert.Equal(6, document.Blocks.Count);
    }

    [Theory]
    [InlineData("Begin forwarded message:")]
    [InlineData("Początek przekazywanej wiadomości:")]
    [InlineData("Anfang der weitergeleiteten Nachricht:")]
    public async Task ProduceAsync_AnAppleMailIntroductionInItsOwnParagraph_BelongsToTheBlockUnderIt(string introduction)
    {
        // Arrange
        var text = $"{introduction}\n\nFrom: Ewa Zielińska\nDate: today\nTo: Marta\n\n{ForwardedWords}";

        // Act
        var document = await MimeFixtures.DocumentOf(Composed(text, html: null));

        // Assert
        Assert.Equal(new MailWrittenHeaders(2, Author, "today"), document.Blocks[0].WrittenHeaders);
        Assert.Equal(ForwardedWords, TextOf(document.Blocks[2]));
    }

    [Fact]
    public async Task ProduceAsync_ABlockInsideAQuotation_NamesItThereAndKeepsTheQuotation()
    {
        // Arrange
        var html = $"<p>See below.</p><blockquote><div>From: Ewa Zielińska<br>Sent: today<br>To: Marta</div><div>{ForwardedWords}</div></blockquote>";

        // Act
        var document = await MimeFixtures.DocumentOf(Composed("x", html));

        // Assert
        var quote = Assert.IsType<MailQuoteBlock>(document.Blocks[1]);
        Assert.Equal(new MailWrittenHeaders(1, Author, "today"), quote.Blocks[0].WrittenHeaders);
        Assert.Equal(ForwardedWords, TextOf(quote.Blocks[1]));
    }

    [Fact]
    public async Task ProduceAsync_APlainTextBlockInsideAQuotedReply_FoldsOnlyTheBlockAndKeepsTheWordsUnderIt()
    {
        // Arrange
        var text = "Thanks.\n\n> -----Original Message-----\n> From: Ewa Zielińska\n> Sent: today\n> To: Marta\n> Subject: CPI\n>\n> Our forecast\n> is 4.1%.";

        // Act
        var document = await MimeFixtures.DocumentOf(Composed(text, html: null));

        // Assert
        Assert.Equal(new MailWrittenHeaders(1, Author, "today"), document.Blocks[1].WrittenHeaders);
        Assert.Equal("> -----Original Message-----\n> From: Ewa Zielińska\n> Sent: today\n> To: Marta\n> Subject: CPI", TextOf(document.Blocks[1]));
        Assert.Equal(">\n> Our forecast\n> is 4.1%.", TextOf(document.Blocks[2]));
    }

    [Theory]
    [MemberData(nameof(RunsNotRecognized))]
    public async Task ProduceAsync_ARunThatDoesNotQualify_IsLeftAsWritten(string run)
    {
        // Act
        var document = await MimeFixtures.DocumentOf(Composed($"{run}\n\n{ForwardedWords}", html: null));

        // Assert
        Assert.All(document.Blocks, block => Assert.Null(block.WrittenHeaders));
        Assert.Equal(run, TextOf(document.Blocks[0]));
    }

    [Fact]
    public async Task ProduceAsync_ARunWithWordsSharingItsParagraph_IsLeftAsWritten()
    {
        // Arrange
        var html = "<div>From: Ewa<br>Date: today<br>To: Marta<br><br>And then the words</div>";

        // Act
        var document = await MimeFixtures.DocumentOf(Composed("x", html));

        // Assert
        Assert.Null(document.Blocks[0].WrittenHeaders);
    }

    private static string TextOf(MailDocumentBlock block) =>
        string.Concat(Assert.IsType<MailParagraphBlock>(block).Content.Select(run => run.Text));

    private static byte[] Composed(string text, string? html)
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

        using var message = new MimeMessage { Subject = "Fwd: CPI", MessageId = "forward-1@example.test", Body = body };
        message.From.Add(new MailboxAddress("Marta", "marta@example.test"));
        message.To.Add(new MailboxAddress("Bob", "bob@example.test"));

        using var written = new MemoryStream();
        message.WriteTo(written);

        return written.ToArray();
    }
}
