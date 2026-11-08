// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using System.Xml.Linq;
using MailFathom.AI.CalendarEvents;
using MailFathom.AI.Orchestration;
using MailFathom.Application.Calendar.Extraction;
using MailFathom.Domain.Calendar;
using Xunit;

namespace MailFathom.AI.UnitTests.CalendarEvents;

/// <summary>Covers what the instruction states and what each of the two turns actually carries.</summary>
/// <remarks>
/// The bounds a model is told about are the ones the reading enforces, so a bound moved in one place and not the
/// other is a failure here rather than an answer quietly cut on every run.
/// </remarks>
public sealed class CalendarEventExtractionInstructionsTests
{
    [Fact]
    public void Text_TheInstruction_StatesTheBoundsTheReadingEnforces()
    {
        // Act
        var text = CalendarEventExtractionInstructions.Text;

        // Assert
        Assert.Contains(
            CalendarEventExtraction.MaximumEvents.ToString(CultureInfo.InvariantCulture),
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            CalendarEventTitle.MaximumLength.ToString(CultureInfo.InvariantCulture),
            text,
            StringComparison.Ordinal);
    }

    /// <summary>The reading refuses an instant carrying a zone, so the instruction has to be the half that says not to write one.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextFor_EitherShapeOfAnswer_AsksForALocalTimeCarryingNoZone(bool constrainedBySchema)
    {
        // Act
        var text = CalendarEventExtractionInstructions.TextFor(constrainedBySchema);

        // Assert
        Assert.Contains("YYYY-MM-DDTHH:MM", text, StringComparison.Ordinal);
        Assert.Contains("no zone offset", text, StringComparison.Ordinal);
    }

    /// <summary>A schema requires every member, so where it shapes the answer an end the text does not state is null rather than left out.</summary>
    [Fact]
    public void TextFor_AnAnswerShapedByTheSchema_AsksForANullEndRatherThanALeftOutOne()
    {
        // Act
        var constrained = CalendarEventExtractionInstructions.TextFor(constrainedBySchema: true);
        var prose = CalendarEventExtractionInstructions.TextFor(constrainedBySchema: false);

        // Assert
        Assert.Contains("It is null otherwise.", constrained, StringComparison.Ordinal);
        Assert.DoesNotContain("Omit it entirely", constrained, StringComparison.Ordinal);
        Assert.Contains("Omit it entirely otherwise.", prose, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposeMailTurn_AMessage_CarriesItsSubjectItsPassagesAndTheInstantToResolveAgainst()
    {
        // Arrange
        var receivedAt = new DateTimeOffset(2026, 9, 21, 9, 30, 0, TimeSpan.FromHours(2));

        // Act
        var turn = CalendarEventExtractionInstructions.ComposeMailTurn(
            "The racking survey",
            receivedAt,
            ["shall we say Thursday at ten", "the yard is open until six"]);

        // Assert
        var message = Parsed(turn).Element(MessagePassages.MessageElementName);
        Assert.Contains("2026-09-21T09:30", turn, StringComparison.Ordinal);
        Assert.Equal("The racking survey", message?.Element(MessagePassages.SubjectElementName)?.Value);
        Assert.Equal(
            ["shall we say Thursday at ten", "the yard is open until six"],
            message?.Elements(MessagePassages.PassageElementName).Select(static passage => passage.Value));
    }

    /// <summary>The offset is the deployment's half of resolving a time, so the message never carries it beside the anchor.</summary>
    [Fact]
    public void ComposeMailTurn_AnArrivalCarryingAnOffset_StatesTheWallClockWithoutIt()
    {
        // Act
        var turn = CalendarEventExtractionInstructions.ComposeMailTurn(
            "A subject",
            new DateTimeOffset(2026, 9, 21, 9, 30, 0, TimeSpan.FromHours(2)),
            ["a passage"]);

        // Assert
        Assert.DoesNotContain("+02:00", turn, StringComparison.Ordinal);
    }

    /// <summary>The message's own arrival, so the same message read again next month resolves Friday to the same day.</summary>
    [Fact]
    public void ComposeMailTurn_TheSameMessageComposedTwice_CarriesTheSameInstant()
    {
        // Arrange
        var receivedAt = new DateTimeOffset(2026, 9, 21, 9, 30, 0, TimeSpan.FromHours(2));

        // Act
        var first = CalendarEventExtractionInstructions.ComposeMailTurn("A subject", receivedAt, ["a passage"]);
        var second = CalendarEventExtractionInstructions.ComposeMailTurn("A subject", receivedAt, ["a passage"]);

        // Assert
        Assert.Equal(first, second);
    }

    [Fact]
    public void ComposeMailTurn_AMessageWithNoSubject_WritesNoSubjectElement()
    {
        // Act
        var turn = CalendarEventExtractionInstructions.ComposeMailTurn(
            subject: null,
            new DateTimeOffset(2026, 9, 21, 9, 30, 0, TimeSpan.Zero),
            ["a passage"]);

        // Assert
        Assert.Empty(Parsed(turn).Descendants(MessagePassages.SubjectElementName));
    }

    /// <summary>The sentence is the person's own words, so it sits in the one element the instruction says speaks for them.</summary>
    [Fact]
    public void ComposeDescriptionTurn_ASentence_CarriesItAsTheAskAndTheInstantToResolveAgainst()
    {
        // Act
        var turn = CalendarEventExtractionInstructions.ComposeDescriptionTurn(
            "lunch with the surveyor tomorrow at one",
            new DateTimeOffset(2026, 9, 21, 9, 30, 0, TimeSpan.FromHours(2)));

        // Assert
        Assert.Contains("2026-09-21T09:30", turn, StringComparison.Ordinal);
        Assert.Contains("Monday", turn, StringComparison.Ordinal);
        Assert.Equal("lunch with the surveyor tomorrow at one", Parsed(turn).Element(AgentTurnDocument.AskElementName)?.Value);
    }

    /// <summary>The offset is the deployment's half of resolving a time, so it is never handed to the model to copy back.</summary>
    [Fact]
    public void ComposeDescriptionTurn_AnInstantCarryingAnOffset_StatesTheWallClockWithoutIt()
    {
        // Act
        var turn = CalendarEventExtractionInstructions.ComposeDescriptionTurn(
            "a sentence",
            new DateTimeOffset(2026, 9, 21, 9, 30, 0, TimeSpan.FromHours(2)));

        // Assert
        Assert.DoesNotContain("+02:00", turn, StringComparison.Ordinal);
    }

    private static XElement Parsed(string turn) => XElement.Parse($"<turn>{turn}</turn>", LoadOptions.PreserveWhitespace);
}
