// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.CalendarEvents;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.Calendar.Extraction;
using Xunit;

namespace MailFathom.AI.UnitTests.CalendarEvents;

/// <summary>Covers the schema an extraction asks a model for: that a provider accepts it, and that nothing it admits is lost.</summary>
public sealed class CalendarEventExtractionSchemaTests
{
    private static readonly DateTimeOffset Anchor = new(2026, 9, 21, 9, 30, 0, TimeSpan.FromHours(2));

    [Fact]
    public void ResponseFormat_TheSchema_KeepsToWhatStrictModeAccepts()
    {
        // Act
        var schema = CalendarEventExtractionSchema.ResponseFormat.Schema!.Value;

        // Assert
        Assert.Empty(StrictSchemaSubset.ViolationsOf(schema));
    }

    /// <summary>
    /// Nothing the schema admits is lost where its strings hold what the instruction asks: every event it admits, with an
    /// end and with a null one, is offered.
    /// </summary>
    [Fact]
    public void Read_EveryDocumentTheSchemaAdmits_OffersEveryEventItHolds()
    {
        // Arrange
        var schema = CalendarEventExtractionSchema.ResponseFormat.Schema!.Value;
        var documents = StrictSchemaSamples.DocumentsAdmittedBy(schema, static member => member switch
        {
            "start" => "2026-09-24T10:00",
            "end" => "2026-09-24T11:00",
            _ => null,
        });

        // Act
        var counts = documents.Select(document => (
            Written: document.Split("\"title\"").Length - 1,
            Read: CalendarEventExtractionReading.Read(document, Anchor, CalendarEventExtraction.MaximumEvents).Count));

        // Assert
        Assert.True(documents.Count > 1);
        Assert.All(counts, static count => Assert.Equal(count.Written, count.Read));
    }
}
