// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Search;
using MailFathom.Application.Retrieval;
using MailFathom.Evaluations.Answering;
using MailFathom.Evaluations.Corpus;
using Xunit;

namespace MailFathom.Evaluations.UnitTests.Answering;

/// <summary>Covers the corpus search answering through the deployment's own search: the filters the answering tool publishes, which a scenario about a person or a period rests on, and the words matching as a lexical search matches them.</summary>
public sealed class CorpusKnowledgeSearchTests
{
    private const string HalinaPettersen = "halina.pettersen@latticeworks.test";

    /// <summary>The evidence <c>DiscoveryEndToEnd.QuotesAMessageWrittenInMarkup</c> holds a result to.</summary>
    private const string BrowserMessage = "timed out while preparing the task set";

    /// <summary>The evidence <c>DiscoveryEndToEnd.NamesTheFix</c> holds a result to.</summary>
    private const string Fix = "queue-handling patch";

    private readonly CorpusKnowledgeSearch search = new(CorpusMessage.All);

    [Fact]
    public async Task FindPassagesAsync_ASenderFilter_ReturnsThatSendersMailAlone()
    {
        // Arrange
        var query = new EmailKnowledgeQuery { QueryText = "pickup", SenderAddress = HalinaPettersen.ToUpperInvariant() };

        // Act
        var found = await this.FindAsync(query);

        // Assert
        Assert.NotEmpty(found);
        Assert.All(found, static message => Assert.Equal(HalinaPettersen, message.Sender));
    }

    [Fact]
    public async Task FindPassagesAsync_AReceivedRange_ReturnsMailInsideItAlone()
    {
        // Arrange
        var after = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var before = new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);
        var query = new EmailKnowledgeQuery { QueryText = "itinerary", ReceivedOnOrAfter = after, ReceivedBefore = before };

        // Act
        var found = await this.FindAsync(query);

        // Assert
        Assert.NotEmpty(found);
        Assert.All(found, message => Assert.InRange(message.ReceivedAt, after, before.AddTicks(-1)));
    }

    [Fact]
    public async Task FindPassagesAsync_ASubjectFragment_ReturnsMailWhoseSubjectCarriesItAlone()
    {
        // Arrange
        var query = new EmailKnowledgeQuery { QueryText = "INV-4827", SubjectFragment = "billing follow-up" };

        // Act
        var found = await this.FindAsync(query);

        // Assert
        Assert.NotEmpty(found);
        Assert.All(found, static message => Assert.Contains("Billing follow-up", message.Subject, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FindPassagesAsync_SeveralWords_ReturnsMailCarryingAnyOneMostWordsFirst()
    {
        // Arrange
        string[] words = ["Lumenfield", "INV-4827"];
        var query = EmailKnowledgeQuery.ForText(string.Join(' ', words));

        // Act
        var found = await this.FindAsync(query);

        // Assert
        int[] carried = [.. found.Select(message => words.Count(word => message.GroundingText.Contains(word, StringComparison.OrdinalIgnoreCase)))];
        Assert.Contains(1, carried);
        Assert.Equal(carried.OrderDescending(), carried);
    }

    /// <summary>The lookup a recorded run wrote for this question, which the correcting message answers without carrying its last word.</summary>
    [Fact]
    public async Task FindPassagesAsync_TheRecordedKestrelQuayLookup_HandsOverTheMessageCorrectingTheMoveDate()
    {
        // Arrange
        var scenario = MailAnsweringScenario.All.Single(static scenario => scenario.Name == "MailAnswering.LaterMessageCorrectsAnEarlierOne");
        var search = new CorpusKnowledgeSearch(scenario.Mailbox);

        // Act
        var lookup = await search.FindPassagesAsync(
            CorpusKnowledgeSearch.Scope,
            EmailKnowledgeQuery.ForText("Kestrel Quay move team"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(lookup.Passages, passage => passage.Text.Contains(scenario.Evidence[0], StringComparison.Ordinal));
    }

    /// <summary>
    /// The lookups gpt-6-luna planned for <c>DiscoveryEndToEnd.QuotesAMessageWrittenInMarkup</c>, every one of which reaches
    /// the message holding the browser's words and none of which hands them over: a message gives a lookup only its few
    /// extracts carrying the most of the lookup's words, and those are the ones describing the export failure.
    /// </summary>
    [Theory]
    [InlineData("QuillDesk CSV export")]
    [InlineData("QuillDesk filtered project")]
    [InlineData("QuillDesk browser error")]
    [InlineData("QuillDesk export failure")]
    [InlineData("QuillDesk CSV export message")]
    [InlineData("QuillDesk filtered project message")]
    [InlineData("QuillDesk filtered project failure")]
    [InlineData("QuillDesk export failed")]
    [InlineData("QuillDesk")]
    public Task FindPassagesAsync_ARecordedQuillDeskLookup_ReachesTheMessageWithoutHandingOverWhatTheBrowserShowed(string queryText) =>
        this.AssertReachedWithoutTheEvidenceAsync(queryText, BrowserMessage);

    /// <summary>
    /// The lookups gpt-6-luna planned for <c>DiscoveryEndToEnd.NamesTheFix</c> in the repetitions that missed it: each
    /// reaches the message naming the fix, and the extracts it hands over describe the spinning panel instead.
    /// </summary>
    [Theory]
    [InlineData("SurveyDesk confirmation panel deploy")]
    [InlineData("SurveyDesk spinning submission patch")]
    [InlineData("SurveyDesk confirmation panel")]
    [InlineData("SurveyDesk deployed submission")]
    [InlineData("SurveyDesk")]
    public Task FindPassagesAsync_ARecordedSurveyDeskLookup_ReachesTheMessageWithoutHandingOverTheFix(string queryText) =>
        this.AssertReachedWithoutTheEvidenceAsync(queryText, Fix);

    /// <summary>The name beside the word the answer stands beside, and nothing else, hands over the sentence holding the answer.</summary>
    [Theory]
    [InlineData("QuillDesk message", BrowserMessage)]
    [InlineData("SurveyDesk patch", Fix)]
    [InlineData("SurveyDesk deployed", Fix)]
    public async Task FindPassagesAsync_TheNameBesideTheWordTheAnswerStandsBeside_HandsOverTheAnswer(string queryText, string evidence)
    {
        // Act
        var lookup = await this.search.FindPassagesAsync(
            CorpusKnowledgeSearch.Scope,
            EmailKnowledgeQuery.ForText(queryText),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(lookup.Passages, passage => passage.Text.Contains(evidence, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The word the mail introduces the browser's message with opens a window that carries the message whole.</summary>
    [Fact]
    public async Task FindPassagesAsync_ALookupCarryingMessageBesideTheProduct_ReachesTheMessageTheBrowserShowed()
    {
        // Act
        var lookup = await this.search.FindPassagesAsync(
            CorpusKnowledgeSearch.Scope,
            EmailKnowledgeQuery.ForText("QuillDesk browser message"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(lookup.Passages, static passage => passage.Text.Contains(BrowserMessage, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FindPassagesAsync_WordsJoinedByOr_ReturnsMailCarryingEitherAlternative()
    {
        // Arrange
        var either = EmailKnowledgeQuery.ForText("Lumenfield OR Pettersen");

        // Act
        var found = await this.FindAsync(either);
        var lumenfield = await this.FindAsync(EmailKnowledgeQuery.ForText("Lumenfield"));
        var pettersen = await this.FindAsync(EmailKnowledgeQuery.ForText("Pettersen"));

        // Assert
        Assert.Contains(found, message => lumenfield.Contains(message));
        Assert.Contains(found, message => pettersen.Contains(message));
    }

    [Fact]
    public async Task FindPassagesAsync_AnExcludedWord_LeavesEveryMessageCarryingItOut()
    {
        // Arrange
        var query = EmailKnowledgeQuery.ForText("INV-4827 -Lumenfield");

        // Act
        var found = await this.FindAsync(query);

        // Assert
        Assert.NotEmpty(found);
        Assert.DoesNotContain(found, static message => message.GroundingText.Contains("Lumenfield", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FindPassagesAsync_AFilterOnServerStateTheCorpusDoesNotCarry_MatchesNothing()
    {
        // Arrange
        var query = new EmailKnowledgeQuery { QueryText = "invoice", IsFlagged = true };

        // Act
        var found = await this.FindAsync(query);

        // Assert
        Assert.Empty(found);
    }

    [Fact]
    public async Task FindPassagesAsync_AnAnswerFarIntoAMessage_ExtractsTheWordsAroundIt()
    {
        // Arrange
        var query = EmailKnowledgeQuery.ForText("Atlas Importer");

        // Act
        var lookup = await this.search.FindPassagesAsync(CorpusKnowledgeSearch.Scope, query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(lookup.Passages, static passage => passage.Text.Contains("Atlas Importer 2.8.4", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FindPassagesAsync_AWordManyMessagesCarry_ExtractsNoMoreOfEachThanADeploymentsSnippets()
    {
        // Arrange
        var query = EmailKnowledgeQuery.ForText("INV-4827");
        var snippets = EmailSearchSnippetBounds.Default;

        // Act
        var lookup = await this.search.FindPassagesAsync(CorpusKnowledgeSearch.Scope, query, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(lookup.Passages);
        Assert.All(lookup.Passages, passage => Assert.InRange(
            passage.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length,
            1,
            snippets.SnippetsPerEmail * snippets.WordsPerSnippet));
    }

    private async Task AssertReachedWithoutTheEvidenceAsync(string queryText, string evidence)
    {
        // Arrange
        var holding = CorpusMessage.All.Single(message => message.GroundingText.Contains(evidence, StringComparison.OrdinalIgnoreCase));

        // Act
        var lookup = await this.search.FindPassagesAsync(
            CorpusKnowledgeSearch.Scope,
            EmailKnowledgeQuery.ForText(queryText),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(lookup.Passages, passage => passage.StoredEmailId == holding.Id);
        Assert.DoesNotContain(lookup.Passages, passage => passage.Text.Contains(evidence, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyList<CorpusMessage>> FindAsync(EmailKnowledgeQuery query)
    {
        var lookup = await this.search.FindPassagesAsync(CorpusKnowledgeSearch.Scope, query, TestContext.Current.CancellationToken);

        return [.. lookup.Passages.Select(static passage => CorpusMessage.All.Single(message => message.Id == passage.StoredEmailId))];
    }
}
