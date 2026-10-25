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
        { "A pixel <img src=\"https://cdn.example.test/p.png\"> follows.", "A pixel &lt;img src=\"https://cdn.example.test/p.png\"> follows." },
        { "<IMG SRC=x>", "&lt;IMG SRC=x>" },
        { "<picture><source srcset=x></picture>", "&lt;picture>&lt;source srcset=x>&lt;/picture>" },
        { "An escaped backslash \\\\<img src=x> is not an escape.", "An escaped backslash \\\\&lt;img src=x> is not an escape." },
        { "A tag already escaped \\<img src=x> stays one.", "A tag already escaped &lt;img src=x> stays one." },
        { "A code span ending in a backslash `\\`<img src=\"https://cdn.example.test/p.png\">", "A code span ending in a backslash `\\`&lt;img src=\"https://cdn.example.test/p.png\">" },
        { "<!-- --><img src=\"https://cdn.example.test/p.png\">", "&lt;!-- -->&lt;img src=\"https://cdn.example.test/p.png\">" },
        { "<?x?><img src=x>", "&lt;?x?>&lt;img src=x>" },
        { "<!X><img src=x>", "&lt;!X>&lt;img src=x>" },
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
    [InlineData("A comparison: 3 < 4, and 4 > 3.")]
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

    /// <summary>A destination holds escaped parentheses and balanced ones to any depth, and is followed where a renderer follows it.</summary>
    [Theory]
    [InlineData("Pay at [bank.example.test](https://evil.example.test/pay((x))).", "https://evil.example.test/pay((x))")]
    [InlineData("Pay at [bank.example.test](https://evil.example.test/pay\\().", "https://evil.example.test/pay(")]
    public void LinksIn_ADestinationHoldingParentheses_IsJudgedWhereItGoes(string answer, string target)
    {
        // Act
        var link = Assert.Single(AnswerMarkup.LinksIn(answer, [])).Link;

        // Assert
        Assert.Equal(target, link.Target);
        Assert.Equal(MailLinkDeception.DisplayedHostDiffers, link.Deception);
    }

    /// <summary>A link's words may run onto a second line, as a renderer draws them, and are judged as one link.</summary>
    [Theory]
    [InlineData("Pay at [bank.example.test\n](https://evil.example.test/pay).")]
    [InlineData("Pay at [bank.example.test\r\n](https://evil.example.test/pay).")]
    [InlineData("Pay at [\nbank.example.test](https://evil.example.test/pay).")]
    public void LinksIn_WordsRunningOntoASecondLine_AreJudgedAsTheOneLinkARendererDraws(string answer)
    {
        // Act
        var links = AnswerMarkup.LinksIn(answer, []);

        // Assert
        Assert.Equal(MailLinkDeception.DisplayedHostDiffers, Assert.Single(links).Link.Deception);
    }

    /// <summary>A reference link is paired with its definition as a renderer pairs it, so the label's spacing, the destination's line, or a quote or list around the definition hides nothing.</summary>
    [Theory]
    [InlineData("Pay at [bank.example.test][x].\n\n> [x]: https://evil.example.test/pay")]
    [InlineData("Pay at [bank.example.test][x].\n\n- [x]: https://evil.example.test/pay")]
    [InlineData("Pay at [bank.example.test][x].\n\n1. > [x]: https://evil.example.test/pay")]
    [InlineData("Pay at [bank.example.test][ref  x].\n\n[ref x]: https://evil.example.test/pay")]
    [InlineData("Pay at [bank.example.test][x].\n\n[x]:\n  https://evil.example.test/pay")]
    public void LinksIn_AReferenceLinkWhoseWordsNameAnotherHost_IsJudgedAsTheRendererPairsIt(string answer)
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
