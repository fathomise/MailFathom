// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Xml.Linq;
using MailFathom.AI.Enrichment;
using MailFathom.AI.Orchestration;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.AI.UnitTests.Enrichment;

/// <summary>Covers what the enrichment agent is told, and what one message reaches it as.</summary>
public sealed class EmailEnrichmentInstructionsTests
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 9, 5, 9, 15, 0, TimeSpan.Zero);

    /// <summary>Every field the reading parses is a field the instruction asked for, or a model is guessing.</summary>
    [Theory]
    [InlineData("sense")]
    [InlineData("significance")]
    [InlineData("commitment")]
    [InlineData("text")]
    [InlineData("reason")]
    [InlineData("passages")]
    [InlineData("dueAt")]
    public void Text_TheInstruction_NamesEveryFieldTheReadingReads(string field)
    {
        // Act
        var text = EmailEnrichmentInstructions.TextFor(MailAccountLanguage.English);

        // Assert
        Assert.Contains(field, text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A derivation is produced for one person and nobody asked it a question, so the language is the reader's rather
    /// than the message's — before their record stated one, a mailbox carrying two languages produced a list that
    /// alternated between them.
    /// </summary>
    [Theory]
    [InlineData(MailAccountLanguage.Polish, "Polish")]
    [InlineData(MailAccountLanguage.English, "English")]
    public void TextFor_TheInstruction_NamesTheLanguageItWasComposedFor(MailAccountLanguage language, string named)
    {
        // Act
        var text = EmailEnrichmentInstructions.TextFor(language);

        // Assert
        Assert.Contains($"Write every sentence you produce in {named}", text, StringComparison.Ordinal);
    }

    /// <summary>A useful newsletter is still not pressing, and a significance on every message is one nobody reads.</summary>
    [Fact]
    public void TextFor_TheInstruction_ReservesSignificanceForWhatIsMorePressingThanUsual()
    {
        // Act
        var text = string.Join(' ', EmailEnrichmentInstructions.TextFor(MailAccountLanguage.English).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        // Assert
        Assert.Contains("is omitted for a message that is merely informative or routine", text, StringComparison.Ordinal);
    }

    /// <summary>A day written only into the sentence is a commitment no reminder can be set from.</summary>
    [Fact]
    public void TextFor_TheInstruction_AsksForTheDueDayWheneverTheMessageNamesOne()
    {
        // Act
        var text = string.Join(' ', EmailEnrichmentInstructions.TextFor(MailAccountLanguage.English).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        // Assert
        Assert.Contains("Write \"dueAt\" whenever the message names the day the commitment falls due", text, StringComparison.Ordinal);
        Assert.Contains("give the earliest of those days", text, StringComparison.Ordinal);
    }

    /// <summary>A line in the reader's own list that leads with what somebody else owes hides what the message asks of them.</summary>
    [Fact]
    public void TextFor_TheInstruction_PutsWhatTheMessageAsksOfItsReaderAheadOfWhatOthersOwe()
    {
        // Act
        var text = string.Join(' ', EmailEnrichmentInstructions.TextFor(MailAccountLanguage.English).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        // Assert
        Assert.Contains("that request is the commitment, ahead of anything the sender or a third party owes", text, StringComparison.Ordinal);
        Assert.Contains("that request and its day are what make it pressing", text, StringComparison.Ordinal);
    }

    /// <summary>The organisation in a signature is often the only party a message names, which is not a reason to make it the one owing.</summary>
    [Fact]
    public void TextFor_TheInstruction_TakesNobodyAsOwingSomethingFromTheSignatureAlone()
    {
        // Act
        var text = string.Join(' ', EmailEnrichmentInstructions.TextFor(MailAccountLanguage.English).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        // Assert
        Assert.Contains("says who wrote the message and nothing more", text, StringComparison.Ordinal);
    }

    /// <summary>A subject rendered into another language is no longer the subject somebody would find in their mail.</summary>
    [Fact]
    public void TextFor_TheInstruction_LeavesQuotedTextAsItWasWritten()
    {
        // Act
        var text = EmailEnrichmentInstructions.TextFor(MailAccountLanguage.Polish);

        // Assert
        Assert.Contains("stays as it was written", text, StringComparison.Ordinal);
    }

    /// <summary>Two languages are two instructions, or a run for one reader would be composed with the other's.</summary>
    [Fact]
    public void TextFor_TheTwoLanguages_ComposeDifferentInstructions()
    {
        // Act
        var polish = EmailEnrichmentInstructions.TextFor(MailAccountLanguage.Polish);
        var english = EmailEnrichmentInstructions.TextFor(MailAccountLanguage.English);

        // Assert
        Assert.NotEqual(polish, english);
    }

    /// <summary>A language this build does not write in is refused rather than falling back to one it does.</summary>
    [Fact]
    public void TextFor_AValueNamingNoLanguage_IsRefused()
    {
        // Act
        var refused = Record.Exception(() => EmailEnrichmentInstructions.TextFor((MailAccountLanguage)99));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(refused);
    }

    /// <summary>A citation is a position in the list this deployment composed, so the turn positions the passages from zero.</summary>
    [Fact]
    public void ComposeEnrichmentTurn_SeveralPassages_PositionsThemFromZeroInOrder()
    {
        // Act
        var turn = EmailEnrichmentInstructions.ComposeEnrichmentTurn(
            "The racking quotation",
            ReceivedAt,
            StubEmailProvenanceReader.KnownCorrespondent,
            ["the quotation is attached", "we need your answer by Friday"]);

        // Assert
        Assert.Equal(
            [("0", "the quotation is attached"), ("1", "we need your answer by Friday")],
            Parsed(turn).Descendants(MessagePassages.PassageElementName)
                .Select(static passage => ((string?)passage.Attribute(MessagePassages.PositionAttributeName), passage.Value)));
    }

    /// <summary>
    /// The arrival instant is the message's own metadata rather than the current time, which is what makes the same
    /// message derived again next month resolve "by Friday" to the same day.
    /// </summary>
    [Fact]
    public void ComposeEnrichmentTurn_AMessageThatArrived_StatesTheArrivalInstant()
    {
        // Act
        var turn = EmailEnrichmentInstructions.ComposeEnrichmentTurn("The racking quotation", ReceivedAt, StubEmailProvenanceReader.KnownCorrespondent, []);

        // Assert
        Assert.Equal(
            "2026-09-05T09:15:00.0000000+00:00",
            (string?)Parsed(turn).Element(MessagePassages.MessageElementName)?.Attribute(MessagePassages.ArrivedAttributeName));
    }

    /// <summary>Where the message came from is written by the service, so a request from an unverifiable sender is not taken as the person's work.</summary>
    [Fact]
    public void ComposeEnrichmentTurn_AMessageFromAFirstContactInJunk_WritesItsProvenanceOnTheMessage()
    {
        // Act
        var turn = EmailEnrichmentInstructions.ComposeEnrichmentTurn(
            "New bank details",
            ReceivedAt,
            StubEmailProvenanceReader.UnverifiedStranger,
            ["our account has changed"]);

        // Assert
        var message = Parsed(turn).Element(MessagePassages.MessageElementName);
        Assert.Equal(
            (AgentTurnDocument.NotEstablishedSender, "true", "true"),
            (
                (string?)message?.Attribute(AgentTurnDocument.SenderAuthenticationAttributeName),
                (string?)message?.Attribute(AgentTurnDocument.JunkAttributeName),
                (string?)message?.Attribute(AgentTurnDocument.FirstContactAttributeName)));
    }

    /// <summary>Absent rather than empty, so the model reads what the message carried instead of a blank it has to tell apart from an empty subject.</summary>
    [Fact]
    public void ComposeEnrichmentTurn_AMessageWithNoSubjectAndNoArrival_WritesNeither()
    {
        // Act
        var message = Parsed(EmailEnrichmentInstructions.ComposeEnrichmentTurn(subject: null, receivedAt: null, StubEmailProvenanceReader.KnownCorrespondent, []))
            .Element(MessagePassages.MessageElementName);

        // Assert
        Assert.NotNull(message);
        Assert.Null(message.Element(MessagePassages.SubjectElementName));
        Assert.Null(message.Attribute(MessagePassages.ArrivedAttributeName));
    }

    /// <summary>The budget is the writer's, so a long message is cut inside a passage and the turn still parses, rather than being cut mid-element afterwards.</summary>
    [Fact]
    public void ComposeEnrichmentTurn_PassagesLongerThanTheBound_StaysWithinItAndStopsAtThePassageItCut()
    {
        // Arrange
        const int Bound = 400;
        var passages = Enumerable.Range(0, 5).Select(static ordinal => new string((char)('a' + ordinal), 200)).ToArray();

        // Act
        var turn = EmailEnrichmentInstructions.ComposeEnrichmentTurn("The racking quotation", ReceivedAt, StubEmailProvenanceReader.KnownCorrespondent, passages, Bound);

        // Assert
        Assert.True(turn.Length <= Bound, $"The turn holds {turn.Length} characters.");
        Assert.Equal(
            "true",
            (string?)Parsed(turn).Descendants(MessagePassages.PassageElementName).Last().Attribute(AgentTurnDocument.CutAttributeName));
    }

    private static XElement Parsed(string turn) => XElement.Parse($"<turn>{turn}</turn>", LoadOptions.PreserveWhitespace);
}
