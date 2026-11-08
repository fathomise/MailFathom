// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using MailFathom.AI.BodyCleanup;
using MailFathom.AI.Orchestration;
using MailFathom.AI.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.AI.UnitTests.BodyCleanup;

/// <summary>Covers the schema a cleaning asks a model for: that a provider accepts it, that its numbers are the outline's own, and that every range it admits reads.</summary>
public sealed class MailBodyCleanupSchemaTests
{
    [Fact]
    public void For_AnOutline_KeepsToWhatStrictModeAccepts()
    {
        // Act
        var schema = MailBodyCleanupSchema.For(blockCount: 3).Schema!.Value;

        // Assert
        Assert.Empty(StrictSchemaSubset.ViolationsOf(schema));
    }

    [Fact]
    public void For_AnOutline_NumbersExactlyItsBlocksAndOffersExactlyTheTwoVerdicts()
    {
        // Act
        var range = RangeOf(MailBodyCleanupSchema.For(blockCount: 3).Schema!.Value);

        // Assert
        Assert.Equal([0, 1, 2], range.GetProperty("from").GetProperty("enum").EnumerateArray().Select(static number => number.GetInt32()));
        Assert.Equal([0, 1, 2], range.GetProperty("to").GetProperty("enum").EnumerateArray().Select(static number => number.GetInt32()));
        Assert.Equal(
            [MailBodyCleanupInstructions.KeepAction, MailBodyCleanupInstructions.DropAction],
            range.GetProperty("action").GetProperty("enum").EnumerateArray().Select(static word => word.GetString()));
    }

    /// <summary>A provider caps what one schema enumerates, so a very long message numbers its blocks as a plain integer the partition check bounds.</summary>
    [Fact]
    public void For_AnOutlinePastWhatOneSchemaEnumerates_NumbersAPlainIntegerInstead()
    {
        // Act
        var schema = MailBodyCleanupSchema.For(StrictJsonSchema.MaximumEnumeratedNumbers + 1).Schema!.Value;

        // Assert
        Assert.False(RangeOf(schema).GetProperty("from").TryGetProperty("enum", out _));
        Assert.Empty(StrictSchemaSubset.ViolationsOf(schema));
    }

    /// <summary>
    /// Every range the schema admits reads into a range, whichever verdict it carries. The one admitted answer the reading
    /// refuses is the empty array, which states no block's fate and so is no partition of anything: the reader is shown
    /// the uncleaned message, as for every answer that does not partition the outline.
    /// </summary>
    [Fact]
    public void Read_EveryDocumentTheSchemaAdmits_ReadsEveryRangeAndRefusesOnlyTheEmptyPartition()
    {
        // Arrange
        var documents = StrictSchemaSamples.DocumentsAdmittedBy(MailBodyCleanupSchema.For(blockCount: 1).Schema!.Value);

        // Act
        var read = documents.ToDictionary(static document => document, static document => MailBodyCleanupReading.Read(document).Count);

        // Assert
        Assert.Equal(0, read["""{"segments":[]}"""]);
        Assert.All(read.Where(static document => document.Key != """{"segments":[]}"""), static document => Assert.Equal(1, document.Value));
        Assert.True(read.Count > 2);
    }

    private static JsonElement RangeOf(JsonElement schema) =>
        schema.GetProperty("properties").GetProperty("segments").GetProperty("items").GetProperty("properties");
}
