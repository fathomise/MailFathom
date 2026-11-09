// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Evaluations.Judging;
using Microsoft.Extensions.AI;
using Xunit;

namespace MailFathom.Evaluations.UnitTests.Judging;

/// <summary>Proves a judge's failed call is reported as a judge that did not answer, while the run's own cancellation still withdraws it.</summary>
public sealed class AnonymousJudgeChatClientTests
{
    [Fact]
    public async Task GetResponseAsync_ATransportTimeoutUnderALiveToken_IsAJudgeThatDidNotAnswer()
    {
        // Arrange
        using var provider = new FailingJudge(new TaskCanceledException("planted-model timed out"));
        using var judge = new AnonymousJudgeChatClient(provider, "juror 1");

        // Act
        var failure = await Assert.ThrowsAsync<JudgeDidNotAnswerException>(
            () => judge.GetResponseAsync("Rate.", cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("The judge (juror 1) did not answer (TaskCanceledException).", failure.Message);
    }

    [Fact]
    public async Task GetResponseAsync_TheRunWithdrawn_PropagatesTheCancellation()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var provider = new FailingJudge(new OperationCanceledException(cancellation.Token));
        using var judge = new AnonymousJudgeChatClient(provider, "juror 1");

        // Act
        var failure = await Record.ExceptionAsync(() => judge.GetResponseAsync("Rate.", cancellationToken: cancellation.Token));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(failure);
    }

    /// <summary>A judge whose every call fails with one planted failure.</summary>
    private sealed class FailingJudge(Exception failure) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ChatResponse>(failure);

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
