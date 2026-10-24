// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Xml.Linq;
using MailFathom.AI.DayLayout;
using MailFathom.AI.Orchestration;
using MailFathom.Application.Tasks;
using MailFathom.Domain.Tasks;
using Xunit;

namespace MailFathom.AI.UnitTests.DayLayout;

/// <summary>Covers what one day reaches the day-layout agent as.</summary>
public sealed class DayLayoutInstructionsTests
{
    private static readonly DateTimeOffset DayStart = new(2026, 9, 21, 7, 0, 0, TimeSpan.Zero);

    /// <summary>A task is cited by the position the turn wrote, and one owed by no day carries no due attribute.</summary>
    [Fact]
    public void ComposeLayoutTurn_TasksOwedOnADayAndOnNone_WritesEachUnderItsPositionAndOnlyTheDueOneWithADay()
    {
        // Arrange
        var question = Day(Owed(dueOn: new DateOnly(2026, 9, 21)), Owed(dueOn: null));

        // Act
        var turn = DayLayoutInstructions.ComposeLayoutTurn(question, ["Confirm the delivery", "Check the itinerary"], []);

        // Assert
        (string?, string?, string?)[] expected = [("0", "2026-09-21", "Confirm the delivery"), ("1", null, "Check the itinerary")];

        Assert.Equal(
            expected,
            Tasks(turn).Select(static task => (
                (string?)task.Attribute(DayLayoutInstructions.PositionAttributeName),
                (string?)task.Attribute(DayLayoutInstructions.DueAttributeName),
                (string?)task.Element(DayLayoutInstructions.TitleElementName))));
    }

    /// <summary>The budget is the writer's, so a list longer than the bound stops at the task it cut and still parses.</summary>
    [Fact]
    public void ComposeLayoutTurn_TasksLongerThanTheBound_StaysWithinItAndWritesNoTaskPastTheCut()
    {
        // Arrange
        const int Bound = 500;
        var question = Day([.. Enumerable.Range(0, 5).Select(static _ => Owed(dueOn: null))]);
        string[] titles = [.. Enumerable.Range(0, 5).Select(static _ => new string('x', 200))];

        // Act
        var turn = DayLayoutInstructions.ComposeLayoutTurn(question, titles, [], Bound);

        // Assert
        Assert.True(turn.Length <= Bound, $"The turn holds {turn.Length} characters.");
        Assert.Equal(
            "true",
            (string?)Tasks(turn).Last().Element(DayLayoutInstructions.TitleElementName)?.Attribute(AgentTurnDocument.CutAttributeName));
    }

    private static DayLayoutQuestion Day(params DayLayoutTask[] tasks) => new(DayStart, DayStart.AddHours(10), tasks, []);

    private static DayLayoutTask Owed(DateOnly? dueOn) => new(PersonalTaskId.Create(Guid.CreateVersion7()), "unused", dueOn);

    private static IEnumerable<XElement> Tasks(string turn) =>
        XElement.Parse($"<turn>{turn}</turn>", LoadOptions.PreserveWhitespace).Descendants(DayLayoutInstructions.TaskElementName);
}
