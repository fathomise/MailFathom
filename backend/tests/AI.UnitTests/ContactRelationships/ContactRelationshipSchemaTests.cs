// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using MailFathom.AI.ContactRelationships;
using MailFathom.AI.Orchestration;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.Contacts.Correspondence;
using MailFathom.Application.Contacts.Relationship;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.AI.UnitTests.ContactRelationships;

/// <summary>Covers the schema a relationship card asks a model for: that a provider accepts it, that its numbers are the correspondence's own, and that nothing it admits is lost.</summary>
public sealed class ContactRelationshipSchemaTests
{
    private static readonly string[] Observations = ["activePeriod", "openItem", "cases"];

    private static readonly DateTimeOffset FirstJuly = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void For_ACorrespondence_KeepsToWhatStrictModeAccepts()
    {
        // Act
        var schema = ContactRelationshipSchema.For(sourceCount: 3).Schema!.Value;

        // Assert
        Assert.Empty(StrictSchemaSubset.ViolationsOf(schema));
    }

    [Fact]
    public void For_ACorrespondence_LimitsEveryCitedNumberToItsOwnConversationsAndDocuments()
    {
        // Act
        var schema = ContactRelationshipSchema.For(sourceCount: 3).Schema!.Value;

        // Assert
        Assert.Equal([0, 1, 2], CitableNumbers(schema).GetProperty("enum").EnumerateArray().Select(static number => number.GetInt32()).ToArray());
    }

    /// <summary>A provider caps what one schema enumerates, so a correspondence past the bound cites a plain integer the reading bounds.</summary>
    [Fact]
    public void For_ACorrespondencePastWhatOneSchemaEnumerates_CitesAPlainIntegerInstead()
    {
        // Act
        var schema = ContactRelationshipSchema.For(StrictJsonSchema.MaximumEnumeratedNumbers + 1).Schema!.Value;

        // Assert
        Assert.False(CitableNumbers(schema).TryGetProperty("enum", out _));
        Assert.Empty(StrictSchemaSubset.ViolationsOf(schema));
    }

    /// <summary>
    /// Nothing the schema admits is lost on the way to a card: a note written with a source is a card, and every other
    /// line written with a source stands on it. The lines dropped are the ones citing nothing, which the schema admits as
    /// an empty array and the reading refuses by design — and a card whose note is null is the empty card.
    /// </summary>
    [Fact]
    public void Read_EveryDocumentTheSchemaAdmits_KeepsEveryLineItCarries()
    {
        // Arrange
        var correspondence = Correspondence();
        var schema = ContactRelationshipSchema.For(correspondence.Threads.Count + correspondence.Documents.Count).Schema!.Value;
        var documents = StrictSchemaSamples.DocumentsAdmittedBy(schema);

        // Act
        var lost = documents.Where(document => !KeepsWhatItCarries(document, ContactRelationshipReading.Read(document, correspondence)));

        // Assert
        Assert.True(documents.Count > 1);
        Assert.Empty(lost);
    }

    private static bool KeepsWhatItCarries(string document, ContactRelationship card)
    {
        using var parsed = JsonDocument.Parse(document);
        var root = parsed.RootElement;

        if (!Cites(root, "note"))
        {
            return !card.WasDerived;
        }

        return card.WasDerived
            && (card.NextAction is not null) == Cites(root, "nextAction")
            && card.Observations.Count == Observations.Count(name => Cites(root, name));
    }

    private static bool Cites(JsonElement root, string field) =>
        root.GetProperty(field) is { ValueKind: JsonValueKind.Object } line
        && line.GetProperty("sources").GetArrayLength() > 0;

    private static JsonElement CitableNumbers(JsonElement schema) =>
        schema.GetProperty("$defs").GetProperty("observation").GetProperty("properties").GetProperty("sources").GetProperty("items");

    private static ContactCorrespondence Correspondence() =>
        new(
            [
                new CorrespondingThread(EmailThreadId.Create(Guid.CreateVersion7()), StoredEmailId.Create(Guid.CreateVersion7()), "the addendum", FirstJuly),
                new CorrespondingThread(EmailThreadId.Create(Guid.CreateVersion7()), StoredEmailId.Create(Guid.CreateVersion7()), "the indexation cap", FirstJuly),
            ],
            [new CorrespondingDocument(StoredEmailId.Create(Guid.CreateVersion7()), 3, "addendum.pdf", "application/pdf", FirstJuly)]);
}
