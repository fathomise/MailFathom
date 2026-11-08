// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.Enrichment;
using MailFathom.AI.Orchestration;
using MailFathom.AI.Search;
using MailFathom.AI.ThreadStates;
using Xunit;

namespace MailFathom.AI.UnitTests.Orchestration;

/// <summary>Covers what is read out of an answer a model wrapped in something else, which every agent here answers through.</summary>
/// <remarks>
/// It is one function rather than one per agent because the shapes a model wraps an object in are the model's habits
/// rather than any one derivation's: a second copy would be the one that stopped handling a shape the first learned.
/// </remarks>
public sealed class AgentJsonAnswerTests
{
    [Theory]
    [InlineData("""{"intent":"findFact"}""")]
    [InlineData("""```json{"intent":"findFact"}```""")]
    [InlineData("""Here it is: {"intent":"findFact"} — I hope it helps.""")]
    public void Unfenced_AnObjectWrittenInsideWhateverAModelWroteAroundIt_ReadsTheObject(string answer)
    {
        // Act
        var json = AgentJsonAnswer.Unfenced(answer);

        // Assert
        Assert.Equal("""{"intent":"findFact"}""", json);
    }

    /// <summary>The outermost braces, so a nested object does not cut the answer short.</summary>
    [Fact]
    public void Unfenced_AnObjectHoldingAnother_ReadsTheWholeOfTheOuterOne()
    {
        // Arrange
        const string answer = """{"filters":{"unread":true},"criteria":["invoice"]}""";

        // Act
        var json = AgentJsonAnswer.Unfenced(answer);

        // Assert
        Assert.Equal(answer, json);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("I am afraid I cannot help with that.")]
    [InlineData("{ an object that never closes")]
    [InlineData("} {")]
    public void Unfenced_AnAnswerHoldingNoObject_ReadsNothing(string? answer)
    {
        // Act, Assert
        Assert.Null(AgentJsonAnswer.Unfenced(answer));
    }

    /// <summary>A member the document declares only inside one nested object, written beside it instead, is read where it belongs.</summary>
    [Fact]
    public void Read_AMemberWrittenBesideTheOneObjectThatDeclaresIt_ReadsItInsideThatObject()
    {
        // Arrange
        const string answer = """{"keepTopic": true, "title": {"standing": "Awaiting the invoice"}}""";

        // Act
        var document = AgentJsonAnswer.Read(answer, ThreadStateJsonContext.Default.ThreadStateDocument);

        // Assert
        Assert.Equal((true, "Awaiting the invoice"), (document?.Title?.KeepTopic, document?.Title?.Standing));
    }

    /// <summary>The policy is the shape's own metadata rather than a list per agent, so a second document gains it without being named.</summary>
    [Fact]
    public void Read_AFilterWrittenBesideTheFilters_ReadsItAmongThem()
    {
        // Arrange
        const string answer = """{"criteria": ["invoice"], "unread": true, "filters": {"flagged": true}}""";

        // Act
        var document = AgentJsonAnswer.Read(answer, MailSearchPhraseJsonContext.Default.MailSearchPhraseDocument);

        // Assert
        Assert.Equal((true, true), (document?.Filters?.Unread, document?.Filters?.Flagged));
    }

    /// <summary>What the nested object wrote for itself is what it says; the copy beside it is ignored.</summary>
    [Fact]
    public void Read_AMemberTheNestedObjectAlsoWrites_KeepsTheNestedObjectsOwnValue()
    {
        // Arrange
        const string answer = """{"keepTopic": true, "title": {"keepTopic": false, "topic": "Racking"}}""";

        // Act
        var document = AgentJsonAnswer.Read(answer, ThreadStateJsonContext.Default.ThreadStateDocument);

        // Assert
        Assert.False(document?.Title?.KeepTopic);
    }

    /// <summary>A null is a member left out, so a nested object writing one is filled from beside it.</summary>
    [Fact]
    public void Read_AMemberTheNestedObjectWritesAsNull_TakesTheValueWrittenBesideIt()
    {
        // Arrange
        const string answer = """{"keepTopic": true, "title": {"keepTopic": null, "standing": ""}}""";

        // Act
        var document = AgentJsonAnswer.Read(answer, ThreadStateJsonContext.Default.ThreadStateDocument);

        // Assert
        Assert.True(document?.Title?.KeepTopic);
    }

    /// <summary>A member placed beside an object the answer never wrote has nowhere to go, and is ignored like an unknown one.</summary>
    [Fact]
    public void Read_AMisplacedMemberWithNoNestedObjectToJoin_IsIgnored()
    {
        // Arrange
        const string answer = """{"keepTopic": true, "agreements": []}""";

        // Act
        var document = AgentJsonAnswer.Read(answer, ThreadStateJsonContext.Default.ThreadStateDocument);

        // Assert
        Assert.NotNull(document);
        Assert.Null(document.Title);
    }

    /// <summary>A member several nested objects declare belongs to none of them in particular, so it is ignored rather than guessed into one.</summary>
    [Fact]
    public void Read_AMemberSeveralNestedObjectsDeclare_IsIgnoredRatherThanGuessedIntoOne()
    {
        // Arrange
        const string answer = """{"text": "Pay the invoice", "sense": {"reason": "It is due"}, "commitment": {}}""";

        // Act
        var document = AgentJsonAnswer.Read(answer, EmailEnrichmentJsonContext.Default.EmailEnrichmentDocument);

        // Assert
        Assert.Equal((null, null), (document?.Sense?.Text, document?.Commitment?.Text));
    }

    /// <summary>A member nothing declares is ignored, so an invented field never costs the rest of the answer.</summary>
    [Fact]
    public void Read_AMemberNothingDeclares_IsIgnoredAndTheRestIsRead()
    {
        // Arrange
        const string answer = """```json {"confidence": "high", "agreements": [{"text": "Settled.", "messages": [0], "certainty": 1}]} ```""";

        // Act
        var document = AgentJsonAnswer.Read(answer, ThreadStateJsonContext.Default.ThreadStateDocument);

        // Assert
        Assert.Equal("Settled.", Assert.Single(document?.Agreements ?? [])?.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("I cannot help with that.")]
    [InlineData("""{"agreements": [}""")]
    [InlineData("""{"keepTopic": true, "title": {"standing": }""")]
    public void Read_AnAnswerHoldingNoObjectThatReads_ReadsNothing(string? answer)
    {
        // Act, Assert
        Assert.Null(AgentJsonAnswer.Read(answer, ThreadStateJsonContext.Default.ThreadStateDocument));
    }
}
