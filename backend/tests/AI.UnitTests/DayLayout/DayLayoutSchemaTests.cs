// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using MailFathom.AI.DayLayout;
using MailFathom.AI.Orchestration;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.Tasks;
using MailFathom.Domain.Tasks;
using Xunit;

namespace MailFathom.AI.UnitTests.DayLayout;

/// <summary>Covers the schema an arrangement asks a model for: that a provider accepts it, that its positions are the day's own, and that nothing it admits is lost.</summary>
public sealed class DayLayoutSchemaTests
{
    private static readonly DateTimeOffset DayStart = new(2026, 9, 21, 8, 0, 0, TimeSpan.Zero);

    private static readonly DayLayoutQuestion Question = new(
        DayStart,
        DayStart.AddHours(12),
        [
            new DayLayoutTask(PersonalTaskId.Create(Guid.NewGuid()), "Answer the supplier", new DateOnly(2026, 9, 21)),
            new DayLayoutTask(PersonalTaskId.Create(Guid.NewGuid()), "Draft the tender response", new DateOnly(2026, 9, 21)),
        ],
        []);

    [Fact]
    public void For_ADay_KeepsToWhatStrictModeAccepts()
    {
        // Act
        var schema = DayLayoutSchema.For(Question.Tasks.Count).Schema!.Value;

        // Assert
        Assert.Empty(StrictSchemaSubset.ViolationsOf(schema));
    }

    [Fact]
    public void For_ADay_LimitsEveryNamedTaskToItsOwnPositions()
    {
        // Act
        var schema = DayLayoutSchema.For(taskCount: 3).Schema!.Value;

        // Assert
        Assert.Equal([0, 1, 2], Positions(PlacedTask(schema)));
        Assert.Equal([0, 1, 2], Positions(DeferredTask(schema)));
    }

    /// <summary>A provider caps what one schema enumerates, so a day past the bound names a plain integer the reading bounds.</summary>
    [Fact]
    public void For_ADayPastWhatOneSchemaEnumerates_NamesAPlainIntegerInstead()
    {
        // Act
        var schema = DayLayoutSchema.For(StrictJsonSchema.MaximumEnumeratedNumbers + 1).Schema!.Value;

        // Assert
        Assert.False(PlacedTask(schema).TryGetProperty("enum", out _));
        Assert.False(DeferredTask(schema).TryGetProperty("enum", out _));
        Assert.Empty(StrictSchemaSubset.ViolationsOf(schema));
    }

    /// <summary>
    /// Nothing the schema admits is lost where its start holds an instant inside the day: every task a document places
    /// is placed, and every task it defers without placing is deferred, read through the deployment's reading.
    /// </summary>
    [Fact]
    public void Read_EveryDocumentTheSchemaAdmits_KeepsEveryTaskItPlacesOrDefers()
    {
        // Arrange
        var schema = DayLayoutSchema.For(Question.Tasks.Count).Schema!.Value;
        var documents = StrictSchemaSamples.DocumentsAdmittedBy(schema, static member => member is "startAt" ? "2026-09-21T10:00:00Z" : null);

        // Act
        var readings = documents.Select(document => (Written: Written(document), Read: DayLayoutReading.Read(document, Question)));

        // Assert
        Assert.True(documents.Count > 1);
        Assert.All(readings, static reading =>
        {
            Assert.Equal(reading.Written.Placed, reading.Read.Placements.Select(static placement => placement.Task).ToHashSet());
            Assert.Equal(reading.Written.Deferred, reading.Read.NotToday.ToHashSet());
        });
    }

    private static (HashSet<PersonalTaskId> Placed, HashSet<PersonalTaskId> Deferred) Written(string document)
    {
        using var parsed = JsonDocument.Parse(document);
        var root = parsed.RootElement;
        var placed = root.GetProperty("placements").EnumerateArray()
            .Select(static placement => Question.Tasks[placement.GetProperty("task").GetInt32()].Id)
            .ToHashSet();
        var deferred = root.GetProperty("notToday").EnumerateArray()
            .Select(static position => Question.Tasks[position.GetInt32()].Id)
            .Where(task => !placed.Contains(task))
            .ToHashSet();

        return (placed, deferred);
    }

    private static JsonElement PlacedTask(JsonElement schema) =>
        schema.GetProperty("properties").GetProperty("placements").GetProperty("items").GetProperty("properties").GetProperty("task");

    private static JsonElement DeferredTask(JsonElement schema) =>
        schema.GetProperty("properties").GetProperty("notToday").GetProperty("items");

    private static int[] Positions(JsonElement position) =>
        [.. position.GetProperty("enum").EnumerateArray().Select(static value => value.GetInt32())];
}
