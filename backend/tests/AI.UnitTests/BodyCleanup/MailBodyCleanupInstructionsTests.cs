// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Xml.Linq;
using MailFathom.AI.BodyCleanup;
using MailFathom.Application.EmailContent.Cleaning;
using Xunit;

namespace MailFathom.AI.UnitTests.BodyCleanup;

/// <summary>Covers the one turn an outline is put to the agent as, and what the instruction is allowed to ask for.</summary>
public sealed class MailBodyCleanupInstructionsTests
{
    [Fact]
    public void ComposeOutlineTurn_AnOutline_WritesTheEnvelopeAboveOneNumberedLinePerBlock()
    {
        // Arrange
        var body = new CleanableMailBody(
            "Your receipt",
            "The Shop",
            [
                new CleanableMailBlock(0, "paragraph", LinkCount: 0, "View this in your browser"),
                new CleanableMailBlock(1, "table", LinkCount: 4, "Shop Deals Account Help"),
            ]);

        // Act
        var turn = MailBodyCleanupInstructions.ComposeOutlineTurn(body);

        // Assert
        Assert.Equal(
            """
            <outline blocks="2">
              <envelope>
                <sender>The Shop</sender>
                <subject>Your receipt</subject>
              </envelope>
              <block number="0" kind="paragraph" links="0">View this in your browser</block>
              <block number="1" kind="table" links="4">Shop Deals Account Help</block>
            </outline>
            """.ReplaceLineEndings("\n"),
            turn);
    }

    /// <summary>Absent rather than empty, so the model reads an envelope that named nobody instead of a blank it has to tell apart from an empty name.</summary>
    [Fact]
    public void ComposeOutlineTurn_AMessageNamingNeitherSenderNorSubject_WritesAnEmptyEnvelope()
    {
        // Arrange
        var body = new CleanableMailBody(
            Subject: null,
            SenderName: null,
            [new CleanableMailBlock(0, "paragraph", LinkCount: 0, "Anything")]);

        // Act
        var envelope = Parsed(MailBodyCleanupInstructions.ComposeOutlineTurn(body)).Descendants(MailBodyCleanupInstructions.EnvelopeElementName);

        // Assert
        Assert.Empty(Assert.Single(envelope).Elements());
    }

    /// <summary>The outline states its whole block count even when the bound cut it, so an answer covering only what fitted fails the partition check.</summary>
    [Fact]
    public void ComposeOutlineTurn_AnOutlineLongerThanTheBound_StaysWithinItAndStillStatesEveryBlock()
    {
        // Arrange
        const int Bound = 500;
        var body = new CleanableMailBody(
            "Your receipt",
            "The Shop",
            [.. Enumerable.Range(0, 20).Select(static index => new CleanableMailBlock(index, "paragraph", LinkCount: 0, new string('o', 80)))]);

        // Act
        var turn = MailBodyCleanupInstructions.ComposeOutlineTurn(body, Bound);

        // Assert
        var outline = Parsed(turn).Element(MailBodyCleanupInstructions.OutlineElementName);
        Assert.True(turn.Length <= Bound, $"The turn holds {turn.Length} characters.");
        Assert.Equal("20", (string?)outline?.Attribute(MailBodyCleanupInstructions.BlockCountAttributeName));
        Assert.InRange(outline?.Elements(MailBodyCleanupInstructions.BlockElementName).Count() ?? 0, 1, 19);
    }

    private static XElement Parsed(string turn) => XElement.Parse($"<turn>{turn}</turn>", LoadOptions.PreserveWhitespace);

    /// <summary>
    /// The instruction asks for ranges and names the two keywords the reading accepts, which is the one place the prose
    /// and the reading have to agree: a renamed keyword would be asked for and then refused on arrival.
    /// </summary>
    [Fact]
    public void Text_TheInstruction_AsksForTheTwoKeywordsTheReadingAccepts()
    {
        // Assert
        Assert.Contains($"\"{MailBodyCleanupInstructions.KeepAction}\"", MailBodyCleanupInstructions.Text, StringComparison.Ordinal);
        Assert.Contains($"\"{MailBodyCleanupInstructions.DropAction}\"", MailBodyCleanupInstructions.Text, StringComparison.Ordinal);
    }
}
