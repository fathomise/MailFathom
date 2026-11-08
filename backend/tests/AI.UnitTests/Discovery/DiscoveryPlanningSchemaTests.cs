// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.Discovery;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.Discovery.Planning;
using MailFathom.Application.Retrieval;
using Xunit;

namespace MailFathom.AI.UnitTests.Discovery;

/// <summary>Covers the schema a plan asks a model for: that a provider accepts it, that its intents are the catalogue's, and that nothing it admits falls back.</summary>
public sealed class DiscoveryPlanningSchemaTests
{
    private static readonly EmailKnowledgeBounds Bounds = EmailKnowledgeBounds.Default;

    private static readonly DateTimeOffset AskedAt = new(2026, 9, 14, 10, 0, 0, TimeSpan.FromHours(2));

    private static readonly MailQuestionText Question = MailQuestionText.Create("which supplier quoted least for the racking");

    [Fact]
    public void ResponseFormat_TheSchema_KeepsToWhatStrictModeAccepts()
    {
        // Act
        var schema = DiscoveryPlanningSchema.ResponseFormat.Schema!.Value;

        // Assert
        Assert.Empty(StrictSchemaSubset.ViolationsOf(schema));
    }

    [Fact]
    public void ResponseFormat_TheSchema_AdmitsEveryIntentTheCatalogueHoldsAndNoOther()
    {
        // Act
        var intents = DiscoveryPlanningSchema.ResponseFormat.Schema!.Value.GetProperty("properties").GetProperty("intent").GetProperty("enum");

        // Assert
        Assert.Equal(DiscoveryIntent.All.Select(static intent => intent.Identity), intents.EnumerateArray().Select(static intent => intent.GetString()));
    }

    /// <summary>
    /// Nothing the schema admits falls back: every filter null or written, every switch either way, the first and the last
    /// intent, and no lookup at all — which keeps its intent and searches the question's own words.
    /// </summary>
    [Fact]
    public void Read_EveryDocumentTheSchemaAdmits_IsReadRatherThanFallenBackFrom()
    {
        // Arrange
        var documents = StrictSchemaSamples.DocumentsAdmittedBy(DiscoveryPlanningSchema.ResponseFormat.Schema!.Value, TextFor);

        // Act
        var outcomes = documents.Select(document => DiscoveryPlanReading.Read(document, Question, Bounds, AskedAt)).ToArray();

        // Assert
        Assert.True(outcomes.Length > 1);
        Assert.All(outcomes, static outcome => Assert.True(outcome.WasRead));
        Assert.Contains(outcomes, static outcome => outcome.Plan.Retrieval.Lookups is [var only] && only.QueryText == Question.Value);
        Assert.Contains(outcomes, static outcome => outcome.Plan.Retrieval.Lookups.Any(static lookup =>
            (lookup.QueryText, lookup.SenderAddress, lookup.ReceivedOnOrAfter, lookup.HasAttachments)
            == ("quotation", "sales@example.test", new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.FromHours(2)), true)));
    }

    /// <summary>
    /// Every answer a model wrote to this agent in the last evaluation run, replayed through the reading. There is one
    /// reading whether or not the request carried a schema, so this is the replay under both.
    /// </summary>
    [Fact]
    public void Read_EveryAnswerRecordedInTheLastEvaluationRun_IsReadRatherThanFallenBackFrom()
    {
        // Arrange
        var recorded = RecordedAnswers.Of("DiscoveryPlanning");

        // Act
        var fallenBack = recorded
            .Where(answer => !DiscoveryPlanReading.Read(answer.Answer, Question, Bounds, AskedAt).WasRead)
            .Select(static answer => answer.Scenario);

        // Assert
        Assert.NotEmpty(recorded);
        Assert.Empty(fallenBack);
    }

    private static string? TextFor(string? member) => member switch
    {
        "queryText" => "quotation",
        "senderAddress" or "recipientAddress" => "sales@example.test",
        "receivedOnOrAfter" => "2026-09-01T00:00",
        "receivedBefore" => "2026-10-01T00:00",
        _ => null,
    };
}
