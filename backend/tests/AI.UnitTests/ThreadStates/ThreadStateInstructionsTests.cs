// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Xml.Linq;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ThreadStates;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.AI.UnitTests.ThreadStates;

/// <summary>Covers what the thread-state agent is told, and what one conversation reaches it as.</summary>
public sealed class ThreadStateInstructionsTests
{
    /// <summary>Every field the reading parses is a field the instruction asked for, or a model is guessing.</summary>
    [Theory]
    [InlineData("agreements")]
    [InlineData("openQuestions")]
    [InlineData("commitments")]
    [InlineData("differences")]
    [InlineData("text")]
    [InlineData("messages")]
    [InlineData("owedBy")]
    [InlineData("dueAt")]
    public void Text_TheInstruction_NamesEveryFieldTheReadingReads(string field)
    {
        // Act
        var text = ThreadStateInstructions.TextFor(MailAccountLanguage.English);

        // Assert
        Assert.Contains(field, text, StringComparison.Ordinal);
    }

    /// <summary>The agent writes down where a conversation stands and never what to do about it.</summary>
    [Fact]
    public void Text_TheInstruction_AsksForNoActionOfAnyKind()
    {
        // Act
        var text = ThreadStateInstructions.TextFor(MailAccountLanguage.English);

        // Assert
        Assert.Contains("not a reply to it", text, StringComparison.Ordinal);
    }

    /// <summary>A payment stated as scheduled is owed by a day, and read as an agreement it loses that day.</summary>
    [Fact]
    public void Text_TheInstruction_RecordsAScheduledActAsACommitment()
    {
        // Act
        var text = string.Join(' ', ThreadStateInstructions.TextFor(MailAccountLanguage.English).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        // Assert
        Assert.Contains("is a commitment even when it is written as a fact rather than as a promise", text, StringComparison.Ordinal);
    }

    /// <summary>A signature names who wrote a message, and an owner read off it attributes a request to the one who made it.</summary>
    [Fact]
    public void Text_TheInstruction_NeverTakesWhoOwesACommitmentFromASignature()
    {
        // Act
        var text = string.Join(' ', ThreadStateInstructions.TextFor(MailAccountLanguage.English).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        // Assert
        Assert.Contains("so it never supplies \"owedBy\" on its own", text, StringComparison.Ordinal);
        Assert.Contains("including one a message asks of the person it is written to", text, StringComparison.Ordinal);
    }

    /// <summary>A citation is a position in the list this deployment composed, so the turn positions the messages.</summary>
    [Fact]
    public void ComposeThreadTurn_SeveralMessages_PositionsThemInTheConversationsOwnOrder()
    {
        // Act
        var turn = ThreadStateInstructions.ComposeThreadTurn(
            "The racking quotation",
            [
                new GuardedThreadMessage(0, "Karolina", Instant(9), "the price holds", StubEmailProvenanceReader.KnownCorrespondent),
                new GuardedThreadMessage(1, "Piotr", Instant(15), "what about the limit", StubEmailProvenanceReader.KnownCorrespondent),
            ]);

        // Assert
        Assert.Equal(
            [("0", "Karolina", "the price holds"), ("1", "Piotr", "what about the limit")],
            Messages(turn).Select(static message => (
                (string?)message.Attribute(ThreadStateInstructions.PositionAttributeName),
                message.Element(ThreadStateInstructions.AuthorElementName)?.Value,
                message.Element(ThreadStateInstructions.TextElementName)?.Value)));
    }

    /// <summary>
    /// Each message carries when it was written, which is what makes "by Friday" resolvable and makes the same
    /// conversation derived again next month resolve it to the same day.
    /// </summary>
    [Fact]
    public void ComposeThreadTurn_AMessageThatWasWritten_StatesWhenItWas()
    {
        // Act
        var turn = ThreadStateInstructions.ComposeThreadTurn(
            "The racking quotation",
            [new GuardedThreadMessage(0, "Karolina", Instant(9), "the price holds", StubEmailProvenanceReader.KnownCorrespondent)]);

        // Assert
        Assert.Equal(
            "2026-09-07T09:00:00.0000000+00:00",
            (string?)Assert.Single(Messages(turn)).Attribute(ThreadStateInstructions.WrittenAttributeName));
    }

    /// <summary>Each message carries where it came from, so a commitment asked for by an unverifiable sender is read as a claim.</summary>
    [Fact]
    public void ComposeThreadTurn_AMessageFromAFirstContactInJunk_WritesItsProvenanceOnThatMessageAlone()
    {
        // Act
        var turn = ThreadStateInstructions.ComposeThreadTurn(
            "New bank details",
            [
                new GuardedThreadMessage(0, "Karolina", Instant(9), "the price holds", StubEmailProvenanceReader.KnownCorrespondent),
                new GuardedThreadMessage(1, "Treasury", Instant(15), "pay the new account today", StubEmailProvenanceReader.UnverifiedStranger),
            ]);

        // Assert
        Assert.Equal(
            new (string?, string?, string?)[] { (AgentTurnDocument.AuthenticatedSender, null, null), (AgentTurnDocument.NotEstablishedSender, "true", "true") },
            Messages(turn).Select(static message => (
                (string?)message.Attribute(AgentTurnDocument.SenderAuthenticationAttributeName),
                (string?)message.Attribute(AgentTurnDocument.JunkAttributeName),
                (string?)message.Attribute(AgentTurnDocument.FirstContactAttributeName))));
    }

    /// <summary>Absent rather than empty, so the model reads what the conversation carried instead of blanks it has to tell apart from empty values.</summary>
    [Fact]
    public void ComposeThreadTurn_AConversationWithNoSubjectAndAMessageWithNoAuthorOrDate_WritesNoneOfThem()
    {
        // Act
        var turn = ThreadStateInstructions.ComposeThreadTurn(
            subject: null,
            [new GuardedThreadMessage(0, null, null, "the price holds", StubEmailProvenanceReader.KnownCorrespondent)]);

        // Assert
        var message = Assert.Single(Messages(turn));
        Assert.Empty(Parsed(turn).Descendants(ThreadStateInstructions.SubjectElementName));
        Assert.Null(message.Element(ThreadStateInstructions.AuthorElementName));
        Assert.Null(message.Attribute(ThreadStateInstructions.WrittenAttributeName));
    }

    /// <summary>The budget is the writer's, so a conversation longer than the bound stops at the message it cut and still parses.</summary>
    [Fact]
    public void ComposeThreadTurn_AConversationLongerThanTheBound_StaysWithinItAndWritesNoMessagePastTheCut()
    {
        // Arrange
        const int Bound = 500;
        GuardedThreadMessage[] messages =
        [
            .. Enumerable.Range(0, 5).Select(static position => new GuardedThreadMessage(position, "Karolina", Instant(9), new string('x', 200), StubEmailProvenanceReader.KnownCorrespondent)),
        ];

        // Act
        var turn = ThreadStateInstructions.ComposeThreadTurn("The racking quotation", messages, Bound);

        // Assert
        Assert.True(turn.Length <= Bound, $"The turn holds {turn.Length} characters.");
        Assert.Equal(
            "true",
            (string?)Messages(turn).Last().Element(ThreadStateInstructions.TextElementName)?.Attribute(AgentTurnDocument.CutAttributeName));
    }

    private static DateTimeOffset Instant(int hour) => new(2026, 9, 7, hour, 0, 0, TimeSpan.Zero);

    private static IEnumerable<XElement> Messages(string turn) => Parsed(turn).Descendants(ThreadStateInstructions.MessageElementName);

    private static XElement Parsed(string turn) => XElement.Parse($"<turn>{turn}</turn>", LoadOptions.PreserveWhitespace);

    /// <summary>
    /// A derivation is produced for one person and nobody asked it a question, so the language is the reader's rather
    /// than the conversation's — before their record stated one, a mailbox carrying two languages produced a block that
    /// alternated between them.
    /// </summary>
    [Theory]
    [InlineData(MailAccountLanguage.Polish, "Polish")]
    [InlineData(MailAccountLanguage.English, "English")]
    public void TextFor_TheInstruction_NamesTheLanguageItWasComposedFor(MailAccountLanguage language, string named)
    {
        // Act
        var text = ThreadStateInstructions.TextFor(language);

        // Assert
        Assert.Contains($"Write every sentence you produce in {named}", text, StringComparison.Ordinal);
    }

    /// <summary>A subject rendered into another language is no longer the subject somebody would find in their mail.</summary>
    [Fact]
    public void TextFor_TheInstruction_LeavesQuotedTextAsItWasWritten()
    {
        // Act
        var text = ThreadStateInstructions.TextFor(MailAccountLanguage.Polish);

        // Assert
        Assert.Contains("stays as it was written", text, StringComparison.Ordinal);
    }

    /// <summary>Two languages are two instructions, or a run for one reader would be composed with the other's.</summary>
    [Fact]
    public void TextFor_TheTwoLanguages_ComposeDifferentInstructions()
    {
        // Act
        var polish = ThreadStateInstructions.TextFor(MailAccountLanguage.Polish);
        var english = ThreadStateInstructions.TextFor(MailAccountLanguage.English);

        // Assert
        Assert.NotEqual(polish, english);
    }

    /// <summary>A language this build does not write in is refused rather than falling back to one it does.</summary>
    [Fact]
    public void TextFor_AValueNamingNoLanguage_IsRefused()
    {
        // Act
        var refused = Record.Exception(() => ThreadStateInstructions.TextFor((MailAccountLanguage)99));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(refused);
    }
}
