// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.Search;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.Emails.Search.Phrasing;
using Xunit;

namespace MailFathom.AI.UnitTests.Search;

/// <summary>Covers the schema a phrase reading asks a model for: that a provider accepts it and that nothing it admits is lost.</summary>
public sealed class MailSearchPhraseSchemaTests
{
    [Fact]
    public void ResponseFormat_TheSchema_KeepsToWhatStrictModeAccepts()
    {
        // Act
        var schema = MailSearchPhraseSchema.ResponseFormat.Schema!.Value;

        // Assert
        Assert.Empty(StrictSchemaSubset.ViolationsOf(schema));
    }

    /// <summary>
    /// Every document the schema admits reads: the one saying nothing — every filter null, every switch false, no criterion
    /// and nothing left over — as the plain word search it stands for, and every other as a reading carrying what it wrote.
    /// </summary>
    [Fact]
    public void Read_EveryDocumentTheSchemaAdmits_ReadsAsWhatItWrote()
    {
        // Arrange
        var documents = StrictSchemaSamples.DocumentsAdmittedBy(MailSearchPhraseSchema.ResponseFormat.Schema!.Value, TextFor);

        // Act
        var readings = documents.Select(MailSearchPhraseDocumentReading.Read).ToArray();

        // Assert
        Assert.Single(readings, static reading => reading == MailSearchPhraseReading.Nothing);
        Assert.All(
            readings.Where(static reading => reading != MailSearchPhraseReading.Nothing),
            static reading => Assert.Equal(
                (true, "anna@example.test", (DateOnly?)new DateOnly(2026, 9, 1), true, "racking quotation", "soon"),
                (reading.WasRead, reading.Filters.SenderAddress, reading.Filters.ReceivedFrom, reading.Filters.HasAttachments, reading.Criteria.Single(), reading.Unaccounted)));
    }

    private static string? TextFor(string? member) => member switch
    {
        "senderAddress" or "recipientAddress" => "anna@example.test",
        "receivedFrom" => "2026-09-01",
        "receivedTo" => "2026-09-30",
        "criteria" => "racking quotation",
        "unaccounted" => "soon",
        _ => null,
    };
}
