// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using MailFathom.AI.ReplyDrafts;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.EmailContent.Rendering.Document.Blocks;
using MailFathom.Application.Emails.ReplyDrafts;
using Xunit;

namespace MailFathom.AI.UnitTests.ReplyDrafts;

/// <summary>Covers how the blocks a model wrote are held to the composer's closed set and to its bounds.</summary>
/// <remarks>
/// Every input here is model output a message can steer. What is asserted is that what fits the closed set is kept,
/// that whatever does not is kept as its words rather than refused, and that a link can only ever be one the turn
/// published.
/// </remarks>
public sealed class ReplyDraftBlockReadingTests
{
    private const string Target = "https://files.example.com/sheet?token=secret";

    [Fact]
    public void Read_APlainDraftOfStrings_ReadsOneParagraphOfOnePlainRunPerString()
    {
        // Act
        var document = Read("""["Thanks, Anna.", "Tuesday works."]""");

        // Assert
        var paragraphs = document.Blocks.Cast<MailParagraphBlock>().ToArray();

        Assert.Equal(["Thanks, Anna.", "Tuesday works."], paragraphs.Select(static paragraph => Assert.Single(paragraph.Content).Text));
        Assert.All(paragraphs.SelectMany(static paragraph => paragraph.Content), static run => Assert.Equal(MailTextEmphasis.None, run.Emphasis));
    }

    [Fact]
    public void Read_ABodyWrittenAsOneString_ReadsAParagraphBetweenEachBlankLine()
    {
        // Act
        var document = Read("\"Thanks, Anna.\\n\\nTuesday works.\\nSee you.\"");

        // Assert
        Assert.Equal(
            ["Thanks, Anna.", "Tuesday works.\nSee you."],
            document.Blocks.Cast<MailParagraphBlock>().Select(static paragraph => Assert.Single(paragraph.Content).Text));
    }

    [Fact]
    public void Read_ABoldLeadInAListAndATableWithAHeader_ReadsEachIntoItsBlock()
    {
        // Arrange
        const string body = """
            [
              { "type": "paragraph", "runs": [{ "text": "Open points", "bold": true }, " are below."] },
              { "type": "list", "ordered": true, "items": ["Price", [{ "text": "Date", "italic": true }]] },
              { "type": "table", "header": true, "rows": [["Plan", "Price"], ["Basic", "10"], ["Pro"]] }
            ]
            """;

        // Act
        var document = Read(body);

        // Assert
        var leadIn = Assert.IsType<MailParagraphBlock>(document.Blocks[0]);
        var list = Assert.IsType<MailListBlock>(document.Blocks[1]);
        var table = Assert.IsType<MailTableBlock>(document.Blocks[2]);

        Assert.Equal(MailTextEmphasis.Bold, leadIn.Content[0].Emphasis);
        Assert.True(list.Ordered);
        Assert.Equal(MailTextEmphasis.Italic, Assert.Single(Assert.IsType<MailParagraphBlock>(list.Items[1].Blocks[0]).Content).Emphasis);
        Assert.Equal([true, false, false], table.Rows.Select(static row => row.IsHeader));
        Assert.Equal(2, table.Columns.Count);
        Assert.All(table.Rows, static row => Assert.Equal(2, row.Cells.Count));
    }

    [Fact]
    public void Read_ALinkCitingAPublishedNumber_LinksTheWordsToThatTarget()
    {
        // Act
        var run = SingleRun("""[{ "type": "paragraph", "runs": [{ "text": "the sheet", "link": 0 }] }]""");

        // Assert
        Assert.Equal(Target, run.Link?.Target);
    }

    [Theory]
    [InlineData("""[{ "type": "paragraph", "runs": [{ "text": "the sheet", "link": 1 }] }]""")]
    [InlineData("""[{ "type": "paragraph", "runs": [{ "text": "the sheet", "link": -1 }] }]""")]
    [InlineData("""[{ "type": "paragraph", "runs": [{ "text": "the sheet", "link": "https://attacker.example/sheet" }] }]""")]
    [InlineData("""[{ "type": "paragraph", "runs": [{ "text": "the sheet", "href": "https://attacker.example/sheet" }] }]""")]
    public void Read_ALinkNamingNoPublishedNumber_KeepsTheWordsAndNoLink(string body)
    {
        // Act
        var run = SingleRun(body);

        // Assert
        Assert.Equal("the sheet", run.Text);
        Assert.Null(run.Link);
    }

    [Theory]
    [InlineData("Red", "#b42318")]
    [InlineData("grey", "#667085")]
    [InlineData("#ff0000", null)]
    [InlineData("crimson", null)]
    public void Read_AColour_KeepsOnlyAPaletteSwatchByName(string written, string? expected)
    {
        // Act
        var run = SingleRun($$"""[{ "type": "paragraph", "runs": [{ "text": "Late", "colour": "{{written}}" }] }]""");

        // Assert
        Assert.Equal(expected, run.Foreground?.Notation);
    }

    [Theory]
    [InlineData("center", MailBlockAlignment.Center)]
    [InlineData("right", MailBlockAlignment.End)]
    [InlineData("diagonal", MailBlockAlignment.Inherited)]
    public void Read_AnAlignment_KeepsOnlyOneOfTheFour(string written, MailBlockAlignment expected)
    {
        // Act
        var document = Read($$"""[{ "type": "paragraph", "align": "{{written}}", "runs": ["Thanks"] }]""");

        // Assert
        Assert.Equal(expected, Assert.IsType<MailParagraphBlock>(Assert.Single(document.Blocks)).Alignment);
    }

    [Theory]
    [InlineData("""[{ "type": "heading", "runs": ["Open points"] }]""")]
    [InlineData("""[{ "type": "image", "text": "Open points", "source": "https://attacker.example/pixel.png" }]""")]
    [InlineData("""[{ "type": "paragraph", "runs": [{ "type": "table", "rows": [["Open points"]] }] }]""")]
    public void Read_AShapeOutsideTheClosedSet_KeepsItsWordsAsAPlainParagraph(string body)
    {
        // Act
        var run = SingleRun(body);

        // Assert
        Assert.Equal("Open points", run.Text);
        Assert.Equal(MailTextEmphasis.None, run.Emphasis);
        Assert.Null(run.Link);
    }

    [Fact]
    public void Read_ATableWiderThanTheBound_ReadsEachRowAsItsWords()
    {
        // Arrange
        var row = string.Join(", ", Enumerable.Range(0, ReplyDraftBlockReading.MaximumTableColumns + 1).Select(static column => $"\"c{column}\""));

        // Act
        var document = Read($$"""[{ "type": "table", "rows": [[{{row}}]] }]""");

        // Assert
        var paragraph = Assert.IsType<MailParagraphBlock>(Assert.Single(document.Blocks));
        var expected = string.Join(ReplyDraft.CellBreak, Enumerable.Range(0, ReplyDraftBlockReading.MaximumTableColumns + 1).Select(static column => $"c{column}"));

        Assert.Equal(expected, Assert.Single(paragraph.Content).Text);
    }

    [Fact]
    public void Read_WordsPastTheDraftsBound_CutsTheRunThatReachesItAndReadsNothingAfter()
    {
        // Arrange
        var filler = new string('a', ReplyDraft.MaximumBodyLength - 10);

        // Act
        var document = Read($$"""["{{filler}}", "{{new string('b', 20)}}", "never read"]""");

        // Assert
        var body = ReplyDraft.Written(document, [], []).Body;

        Assert.True(document.Truncated);
        Assert.Equal(2, document.Blocks.Count);
        Assert.InRange(body.Length, ReplyDraft.MaximumBodyLength - ReplyDraft.BlockBreak.Length, ReplyDraft.MaximumBodyLength);
        Assert.EndsWith("b", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_AListPastTheDraftsBound_KeepsItsPlainReadingWithinTheBoundAndWhole()
    {
        // Arrange
        var items = string.Join(", ", Enumerable.Range(0, ReplyDraftBlockReading.MaximumListItems).Select(static item => $"\"{new string('a', 99)}{item % 10}\""));

        // Act
        var document = Read($$"""[{ "type": "list", "ordered": true, "items": [{{items}}] }]""");

        // Assert
        var list = Assert.IsType<MailListBlock>(Assert.Single(document.Blocks));
        var lastItem = Assert.Single(Assert.IsType<MailParagraphBlock>(Assert.Single(list.Items[^1].Blocks)).Content).Text;
        var body = ReplyDraft.Written(document, [], []).Body;

        Assert.True(document.Truncated);
        Assert.True(body.Length <= ReplyDraft.MaximumBodyLength);
        Assert.EndsWith(lastItem, body, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_ARunOfSpacesBetweenTwoRuns_KeepsTheWordsApart()
    {
        // Act
        var document = Read("""[{ "type": "paragraph", "runs": [{ "text": "Total:", "bold": true }, " ", "42"] }]""");

        // Assert
        var paragraph = Assert.IsType<MailParagraphBlock>(Assert.Single(document.Blocks));

        Assert.Equal("Total: 42", string.Concat(paragraph.Content.Select(static run => run.Text)));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""["   ", { "type": "paragraph", "runs": [] }]""")]
    [InlineData("""[{ "type": "paragraph", "runs": [" "] }]""")]
    [InlineData("""[{ "type": "list", "items": [] }]""")]
    [InlineData("""[{ "type": "list", "items": ["", " "] }]""")]
    [InlineData("""[{ "type": "table", "rows": [[""]] }]""")]
    [InlineData("""[{ "type": "table", "rows": [[]] }]""")]
    [InlineData("42")]
    public void Read_ABodyHoldingNoWords_ReadsNothing(string body)
    {
        // Act
        var document = ReplyDraftBlockReading.Read(JsonDocument.Parse(body).RootElement, Links());

        // Assert
        Assert.Null(document);
    }

    private static MailDocument Read(string body) =>
        ReplyDraftBlockReading.Read(JsonDocument.Parse(body).RootElement, Links())
        ?? throw new InvalidOperationException("The body read as nothing.");

    private static MailInlineRun SingleRun(string body) =>
        Assert.Single(Assert.IsType<MailParagraphBlock>(Assert.Single(Read(body).Blocks)).Content);

    private static IReadOnlyList<ReplyDraftLink> Links() =>
        [new ReplyDraftLink(0, MailLinkReader.Read(Target, "comparison sheet")!, "comparison sheet")];
}
