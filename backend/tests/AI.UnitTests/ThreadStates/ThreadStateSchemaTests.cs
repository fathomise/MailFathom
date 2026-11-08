// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ThreadStates;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.Emails.ThreadStates;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.AI.UnitTests.ThreadStates;

/// <summary>Covers the schema a derivation asks a model for: that a provider accepts it, that its positions are the conversation's own, and that nothing it admits is withheld.</summary>
public sealed class ThreadStateSchemaTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void For_EitherShapeOfTitle_KeepsToWhatStrictModeAccepts(bool topicShown)
    {
        // Act
        var schema = ThreadStateSchema.For(messageCount: 3, topicShown).Schema!.Value;

        // Assert
        Assert.Empty(StrictSchemaSubset.ViolationsOf(schema));
    }

    [Fact]
    public void For_AConversation_LimitsEveryCitedPositionToItsOwnMessages()
    {
        // Act
        var schema = ThreadStateSchema.For(messageCount: 3, topicShown: false).Schema!.Value;

        // Assert
        Assert.Equal([0, 1, 2], CitablePositions(schema, "statement"));
        Assert.Equal([0, 1, 2], CitablePositions(schema, "commitment"));
    }

    /// <summary>A provider caps what one schema enumerates, so a conversation past the bound cites a plain integer the reading bounds.</summary>
    [Fact]
    public void For_AConversationPastWhatOneSchemaEnumerates_CitesAPlainIntegerInstead()
    {
        // Act
        var schema = ThreadStateSchema.For(StrictJsonSchema.MaximumEnumeratedNumbers + 1, topicShown: false).Schema!.Value;

        // Assert
        var cited = schema.GetProperty("$defs").GetProperty("statement").GetProperty("properties").GetProperty("messages").GetProperty("items");

        Assert.False(cited.TryGetProperty("enum", out _));
        Assert.Empty(StrictSchemaSubset.ViolationsOf(schema));
    }

    /// <summary>Only a turn that showed a topic offers one to keep, so the title asks for a topic outright where it showed none.</summary>
    [Theory]
    [InlineData(false, new[] { "standing", "subjectChanged", "topic" })]
    [InlineData(true, new[] { "keepTopic", "standing", "subjectChanged", "topic" })]
    public void For_ATurnThatDidOrDidNotShowATopic_OffersKeepingOneOnlyWhereItDid(bool topicShown, string[] members)
    {
        // Act
        var schema = ThreadStateSchema.For(messageCount: 1, topicShown).Schema!.Value;

        // Assert
        var title = schema.GetProperty("properties").GetProperty("title").GetProperty("properties");

        Assert.Equal(members, title.EnumerateObject().Select(static member => member.Name).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Nothing the schema admits is withheld: every nullable member as null, every array empty and full, every boolean
    /// either way, and every position at the ends of the conversation, read through the deployment's reading.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Read_EveryDocumentTheSchemaAdmits_SettlesRatherThanBeingWithheld(bool topicShown)
    {
        // Arrange
        var messages = Messages(3);
        var schema = ThreadStateSchema.For(messages.Count, topicShown).Schema!.Value;
        var documents = StrictSchemaSamples.DocumentsAdmittedBy(schema, static member => member is "dueAt" ? "2026-09-10" : null);

        // Act
        var withheld = documents.Where(document => !ThreadStateReading.Read(document, messages, topicShown).IsSettled);

        // Assert
        Assert.True(documents.Count > 1);
        Assert.Empty(withheld);
    }

    private static int[] CitablePositions(JsonElement schema, string definition) =>
    [
        .. schema.GetProperty("$defs").GetProperty(definition).GetProperty("properties").GetProperty("messages")
            .GetProperty("items").GetProperty("enum").EnumerateArray().Select(static position => position.GetInt32()),
    ];

    private static IReadOnlyList<DerivableThreadMessage> Messages(int count) =>
    [
        .. Enumerable.Range(0, count).Select(static position => new DerivableThreadMessage(
            StoredEmailId.Create(Guid.CreateVersion7()),
            position,
            "Karolina",
            new DateTimeOffset(2026, 9, 7, 9, 0, 0, TimeSpan.Zero).AddHours(position),
            "the price holds",
            StubEmailProvenanceReader.KnownCorrespondent)),
    ];
}
