// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.Decisions;
using MailFathom.AI.UnitTests.TestDoubles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Xunit;

namespace MailFathom.AI.UnitTests.Decisions;

/// <summary>Covers the cache an evaluation run files decision answers in: a request asked once is answered from it, and any change to what is asked asks again.</summary>
public sealed class DistributedCachingDecisionClientTests
{
    private static readonly ChatMessage[] Input = [new(ChatRole.User, "Your parcel is waiting, pay the fee here.")];

    private static readonly DecisionQuestion[] Questions =
    [
        DecisionQuestion.YesNo("spam", "Is this unsolicited bulk or deceptive mail?"),
        DecisionQuestion.Choice("kind", "What kind of mail is it?", [new("phishing", "Asks for credentials or payment."), new("ordinary", string.Empty)]),
    ];

    [Fact]
    public async Task DecideAsync_TheSameRequestTwice_AsksTheModelOnceAndReadsTheSameAnswerBack()
    {
        // Arrange
        using var model = ScriptedDecisionClient.AnsweringFirstOption(inputTokens: 40, outputTokens: 0);
        using var client = new DistributedCachingDecisionClient(model, new DictionaryCache());

        // Act
        var first = await client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken);
        var second = await client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(model.Calls);
        Assert.Equal(
            first.Answers.Select(answer => (answer.QuestionId, answer.Value, answer.Probabilities[answer.Value!])),
            second.Answers.Select(answer => (answer.QuestionId, answer.Value, answer.Probabilities[answer.Value!])));
        Assert.Equal(first.ModelId, second.ModelId);
        Assert.Equal(40, second.Usage?.InputTokenCount);
    }

    /// <summary>A question is part of what the model is asked, so rewording one must never read back the old answer.</summary>
    [Fact]
    public async Task DecideAsync_AChangedQuestion_AsksTheModelAgain()
    {
        // Arrange
        using var model = ScriptedDecisionClient.AnsweringFirstOption();
        using var client = new DistributedCachingDecisionClient(model, new DictionaryCache());
        DecisionQuestion[] reworded = [DecisionQuestion.YesNo("spam", "Would the recipient call this spam?"), Questions[1]];

        await client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await client.DecideAsync(Input, reworded, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, model.Calls.Count);
    }

    [Fact]
    public async Task DecideAsync_AChangedInputModelOrAdditionalValue_AsksTheModelAgain()
    {
        // Arrange
        using var model = ScriptedDecisionClient.AnsweringFirstOption();
        var cache = new DictionaryCache();
        using var client = new DistributedCachingDecisionClient(model, cache) { CacheKeyAdditionalValues = ["vendor/decider-a"] };
        using var elsewhere = new DistributedCachingDecisionClient(model, cache) { CacheKeyAdditionalValues = ["vendor/decider-b"] };

        await client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await client.DecideAsync([new(ChatRole.User, "Minutes of Tuesday's meeting attached.")], Questions, cancellationToken: TestContext.Current.CancellationToken);
        await client.DecideAsync(Input, Questions, new DecisionOptions { ModelId = "vendor/decider-c" }, TestContext.Current.CancellationToken);
        await elsewhere.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(4, model.Calls.Count);
    }

    private sealed class DictionaryCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> entries = new(StringComparer.Ordinal);

        public byte[]? Get(string key) => this.entries.GetValueOrDefault(key);

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(this.Get(key));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => this.entries[key] = value;

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            this.Set(key, value, options);

            return Task.CompletedTask;
        }

        public void Refresh(string key)
        {
            // Nothing expires here.
        }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key) => this.entries.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            this.Remove(key);

            return Task.CompletedTask;
        }
    }
}
