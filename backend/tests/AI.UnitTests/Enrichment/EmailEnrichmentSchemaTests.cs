// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using MailFathom.AI.Enrichment;
using MailFathom.AI.Orchestration;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Enrichment;
using Xunit;

namespace MailFathom.AI.UnitTests.Enrichment;

/// <summary>Covers the schema an enrichment asks a model for: that a provider accepts it, that its positions are the message's own, and that nothing it admits is lost.</summary>
public sealed class EmailEnrichmentSchemaTests
{
    private static readonly string[] Readings = ["sense", "significance", "commitment"];

    [Fact]
    public void For_AMessage_KeepsToWhatStrictModeAccepts()
    {
        // Act
        var schema = EmailEnrichmentSchema.For(passageCount: 3).Schema!.Value;

        // Assert
        Assert.Empty(StrictSchemaSubset.ViolationsOf(schema));
    }

    [Fact]
    public void For_AMessage_LimitsEveryCitedPositionToItsOwnPassages()
    {
        // Act
        var schema = EmailEnrichmentSchema.For(passageCount: 3).Schema!.Value;

        // Assert
        Assert.Equal([0, 1, 2], CitablePositions(schema, "reading"));
        Assert.Equal([0, 1, 2], CitablePositions(schema, "commitment"));
    }

    /// <summary>A provider caps what one schema enumerates, so a message past the bound cites a plain integer the reading bounds.</summary>
    [Fact]
    public void For_AMessagePastWhatOneSchemaEnumerates_CitesAPlainIntegerInstead()
    {
        // Act
        var schema = EmailEnrichmentSchema.For(StrictJsonSchema.MaximumEnumeratedNumbers + 1).Schema!.Value;

        // Assert
        var cited = schema.GetProperty("$defs").GetProperty("reading").GetProperty("properties").GetProperty("passages").GetProperty("items");

        Assert.False(cited.TryGetProperty("enum", out _));
        Assert.Empty(StrictSchemaSubset.ViolationsOf(schema));
    }

    /// <summary>
    /// Nothing the schema admits is lost on the way to a mark: every reading written with a passage becomes a mark, every
    /// task becomes a proposal, and every day written becomes the day it names. The one reading dropped is the one citing
    /// no passage, which the schema admits as an empty array and the reading refuses by design.
    /// </summary>
    [Fact]
    public void Read_EveryDocumentTheSchemaAdmits_KeepsEveryReadingAndTaskItCarries()
    {
        // Arrange
        var passages = Passages(3);
        var schema = EmailEnrichmentSchema.For(passages.Count).Schema!.Value;
        var documents = StrictSchemaSamples.DocumentsAdmittedBy(schema, static member => member is "dueAt" or "dueOn" ? "2026-09-10" : null);

        // Act
        var lost = documents.Where(document => !KeepsWhatItCarries(document, EmailEnrichmentReading.Read(document, passages, EmailEnrichmentAgentComposition.AgentName)));

        // Assert
        Assert.True(documents.Count > 1);
        Assert.Empty(lost);
    }

    private static bool KeepsWhatItCarries(string document, EmailEnrichmentDerivation derivation)
    {
        using var parsed = JsonDocument.Parse(document);
        var root = parsed.RootElement;

        var citedReadings = Readings.Count(name =>
            root.GetProperty(name) is { ValueKind: JsonValueKind.Object } reading
            && reading.GetProperty("passages").GetArrayLength() > 0);
        var tasks = root.GetProperty("tasks").EnumerateArray().ToArray();
        var datedTasks = tasks.Count(static task => task.GetProperty("dueOn").ValueKind is JsonValueKind.String);

        return derivation.IsSettled
            && derivation.Marks.Count == citedReadings
            && derivation.Tasks.Count == tasks.Length
            && derivation.Tasks.Count(static proposal => proposal.DueOn is not null) == datedTasks;
    }

    private static int[] CitablePositions(JsonElement schema, string definition) =>
    [
        .. schema.GetProperty("$defs").GetProperty(definition).GetProperty("properties").GetProperty("passages")
            .GetProperty("items").GetProperty("enum").EnumerateArray().Select(static position => position.GetInt32()),
    ];

    private static IReadOnlyList<EnrichablePassage> Passages(int count) =>
    [
        .. Enumerable.Range(0, count).Select(static ordinal => new EnrichablePassage(
            EmailChunkId.Create(Guid.CreateVersion7()),
            ordinal,
            "the quotation is attached")),
    ];
}
