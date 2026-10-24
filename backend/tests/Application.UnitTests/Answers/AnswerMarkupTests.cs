// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Answers;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Retrieval;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authorship;
using Xunit;

namespace MailFathom.Application.UnitTests.Answers;

/// <summary>Covers what the service lets an AI answer carry: no picture, and every link judged and traced to the cited mail.</summary>
public sealed class AnswerMarkupTests
{
    public static TheoryData<string, string> Pictures => new()
    {
        { "See ![the badge](https://cdn.example.test/badge.png) here.", "See [the badge](https://cdn.example.test/badge.png) here." },
        { "![](https://cdn.example.test/p.png)", "[](https://cdn.example.test/p.png)" },
        { "Twice !![a](https://cdn.example.test/p.png) over.", "Twice [a](https://cdn.example.test/p.png) over." },
        { "Logo: ![logo][1]\n\n[1]: https://cdn.example.test/l.png", "Logo: [logo][1]\n\n[1]: https://cdn.example.test/l.png" },
        { "A pixel <img src=\"https://cdn.example.test/p.png\"> follows.", "A pixel \\<img src=\"https://cdn.example.test/p.png\"> follows." },
        { "<IMG SRC=x>", "\\<IMG SRC=x>" },
        { "<picture><source srcset=x></picture>", "\\<picture>\\<source srcset=x>\\</picture>" },
        { "An escaped backslash \\\\<img src=x> is not an escape.", "An escaped backslash \\\\\\<img src=x> is not an escape." },
    };

    [Theory]
    [MemberData(nameof(Pictures))]
    public void WithoutPictures_PictureMarkup_IsLeftWithNothingARendererFetches(string answer, string expected)
    {
        // Act
        var published = AnswerMarkup.WithoutPictures(answer);

        // Assert
        Assert.Equal(expected, published);
    }

    [Theory]
    [InlineData("Plain prose with **bold** and a [link](https://example.test).")]
    [InlineData("An autolink <https://example.test/a> and a mailbox <ana@example.test>.")]
    [InlineData("A comparison: 3 < 4 and a tag already escaped \\<img src=x>.")]
    public void WithoutPictures_NoPictureMarkup_LeavesTheAnswerAsWritten(string answer)
    {
        // Act
        var published = AnswerMarkup.WithoutPictures(answer);

        // Assert
        Assert.Equal(answer, published);
    }

    [Fact]
    public void LinksIn_WordsNamingAnotherHost_AreJudgedAsAMailLinkIs()
    {
        // Act
        var links = AnswerMarkup.LinksIn("Pay at [bank.example.test](https://evil.example.test/pay).", []);

        // Assert
        var link = Assert.Single(links);
        Assert.Equal("https://evil.example.test/pay", link.Link.Target);
        Assert.Equal(MailLinkDeception.DisplayedHostDiffers, link.Link.Deception);
        Assert.True(link.Link.IsWorthWarningAbout);
    }

    /// <summary>A link is judged by the words a renderer shows, so marks around the host it names change nothing.</summary>
    [Theory]
    [InlineData("Pay at [**bank.example.test**](https://evil.example.test/pay).")]
    [InlineData("Pay at [`bank.example.test`](https://evil.example.test/pay).")]
    [InlineData("Pay at [bank\\.example\\.test](https://evil.example.test/pay).")]
    [InlineData("Pay at [bank&#46;example.test](https://evil.example.test/pay).")]
    [InlineData("Pay at [~~_bank.example.test_~~](https://evil.example.test/pay).")]
    public void LinksIn_MarkedUpWordsNamingAnotherHost_AreJudgedAsTheirPlainFormIs(string answer)
    {
        // Act
        var links = AnswerMarkup.LinksIn(answer, []);

        // Assert
        Assert.Equal(MailLinkDeception.DisplayedHostDiffers, Assert.Single(links).Link.Deception);
    }

    [Fact]
    public void LinksIn_AHostInALookalikeScript_CarriesItsAsciiForm()
    {
        // Act
        var links = AnswerMarkup.LinksIn("Open [the portal](https://pаypal.example.test/).", []);

        // Assert
        var link = Assert.Single(links);
        Assert.NotNull(link.Link.AsciiHost);
        Assert.True(link.Link.IsWorthWarningAbout);
    }

    [Fact]
    public void LinksIn_EveryWayMarkdownWritesALink_IsFound()
    {
        // Arrange
        const string answer = """
            An [inline](https://a.example.test/one "title") link, an autolink <https://b.example.test/>, a mailbox
            <ana@example.test>, a bare https://c.example.test/path. and www.d.example.test, and [a reference][ref].

            [ref]: https://e.example.test/
            """;

        // Act
        var targets = AnswerMarkup.LinksIn(answer, []).Select(static link => link.Link.Target).ToArray();

        // Assert
        Assert.Equal(
            [
                "https://a.example.test/one",
                "https://b.example.test/",
                "mailto:ana@example.test",
                "https://c.example.test/path",
                "http://www.d.example.test/",
                "https://e.example.test/",
            ],
            targets);
    }

    [Fact]
    public void LinksIn_ATargetWrittenHonestlyAndDeceptively_KeepsTheDeceptiveJudgement()
    {
        // Act
        var links = AnswerMarkup.LinksIn(
            "[evil.example.test](https://evil.example.test/) or [bank.example.test](https://evil.example.test/).",
            []);

        // Assert
        Assert.Equal(MailLinkDeception.DisplayedHostDiffers, Assert.Single(links).Link.Deception);
    }

    [Fact]
    public void LinksIn_ASchemeNoReaderMayFollow_IsNotALink()
    {
        // Act
        var links = AnswerMarkup.LinksIn("[run](javascript:alert(1)) and [file](file:///etc/passwd)", []);

        // Assert
        Assert.Empty(links);
    }

    [Fact]
    public void LinksIn_AnAddressTheCitedMailCarries_AppearsInCitedSources()
    {
        // Arrange
        IReadOnlyList<string> cited = ["The agenda is at HTTPS://Agenda.Example.test/day-one, see you there."];

        // Act
        var links = AnswerMarkup.LinksIn("The [agenda](https://agenda.example.test/day-one).", cited);

        // Assert
        Assert.True(Assert.Single(links).AppearsInCitedSources);
    }

    [Theory]
    [InlineData("Nothing in the mail carries an address.")]
    [InlineData("The mail names https://agenda.example.test/day-one-and-more instead.")]
    [InlineData("The mail names https://agenda.example.test.evil.test/day-one instead.")]
    public void LinksIn_AnAddressNoCitedSourceCarries_DoesNotAppearInCitedSources(string source)
    {
        // Act
        var links = AnswerMarkup.LinksIn("The [agenda](https://agenda.example.test/day-one).", [source]);

        // Assert
        Assert.False(Assert.Single(links).AppearsInCitedSources);
    }

    [Fact]
    public void LinksIn_AMailboxTheCitedMailNames_AppearsInCitedSourcesWhateverTheLinkAddsToIt()
    {
        // Act
        var links = AnswerMarkup.LinksIn("Write to [Ana](mailto:ana@example.test?subject=Hello).", ["Contact ana@example.test."]);

        // Assert
        Assert.True(Assert.Single(links).AppearsInCitedSources);
    }

    [Fact]
    public void LinksIn_MoreTargetsThanTheBound_JudgesOnlyTheFirst()
    {
        // Arrange
        var answer = string.Join(
            ' ',
            Enumerable.Range(0, AnswerMarkup.MaximumLinks + 5)
                .Select(static at => at % 2 is 0 ? $"https://example.test/{at}" : $"[link {at}](https://example.test/{at})"));

        // Act
        var links = AnswerMarkup.LinksIn(answer, []);

        // Assert
        Assert.Equal(
            Enumerable.Range(0, AnswerMarkup.MaximumLinks).Select(static at => $"https://example.test/{at}"),
            links.Select(static link => link.Link.Target));
    }

    [Fact]
    public void TextOf_APassageCarryingFiles_ReadsTheExtractAndEveryFile()
    {
        // Arrange
        var passage = new EmailKnowledgePassage
        {
            StoredEmailId = StoredEmailId.Create(Guid.CreateVersion7()),
            AccountId = MailAccountId.Create("work"),
            Folder = SyntheticEmailSummaries.FolderOf("work", "inbox"),
            SenderVerification = SenderVerification.NotEstablished,
            MachineAuthorship = MachineAuthorshipAssessment.NotAssessed,
            Text = "The covering note.",
            AttachmentExtracts = [new EmailKnowledgeAttachmentExtract(0, "terms.pdf", AttachmentTextKind.Document, null, "The terms.")],
        };

        // Act
        var texts = AnswerMarkup.TextOf([passage]);

        // Assert
        Assert.Equal(["The covering note.", "The terms."], texts);
    }
}
