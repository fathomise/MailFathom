// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using System.Net;
using MailFathom.Evaluations.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Evaluations.UnitTests.Providers;

/// <summary>Proves a provider call is asked again only when the provider failed transiently, and only up to its bound.</summary>
public sealed class TransientProviderRetryChatClientTests
{
    private const string Answer = "The export failed on build 4.8.2.";

    private static readonly TimeSpan Step = TimeSpan.FromSeconds(1);

    /// <summary>Slots enough that no test here waits for one, shared because a semaphore no call waits on holds nothing.</summary>
    private static readonly SemaphoreSlim Unlimited = new(int.MaxValue);

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task GetResponseAsync_AProviderThatWasBusy_AsksAgainAndReturnsTheLaterAnswer(HttpStatusCode status)
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var provider = new ScriptedProvider(clock, new HttpRequestException("busy", inner: null, status));
        using var client = new TransientProviderRetryChatClient(provider, clock, Unlimited);

        // Act
        var response = await AdvanceUntilCompletedAsync(clock, AskAsync(client, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(Answer, response.Text);
        Assert.Equal(2, provider.Calls);
    }

    [Fact]
    public async Task GetResponseAsync_ARequestAbandonedAtTheTransportTimeout_AsksAgainAndReturnsTheLaterAnswer()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var provider = new ScriptedProvider(clock, new TaskCanceledException("timed out", new TimeoutException()));
        using var client = new TransientProviderRetryChatClient(provider, clock, Unlimited);

        // Act
        var response = await AdvanceUntilCompletedAsync(clock, AskAsync(client, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(Answer, response.Text);
        Assert.Equal(2, provider.Calls);
    }

    [Fact]
    public async Task GetResponseAsync_AConnectionThatFailedBeforeAnyAnswer_AsksAgainAndReturnsTheLaterAnswer()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var provider = new ScriptedProvider(clock, new HttpRequestException("connection reset"));
        using var client = new TransientProviderRetryChatClient(provider, clock, Unlimited);

        // Act
        var response = await AdvanceUntilCompletedAsync(clock, AskAsync(client, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(Answer, response.Text);
        Assert.Equal(2, provider.Calls);
    }

    [Fact]
    public async Task GetResponseAsync_ARateLimitedCall_WaitsBeforeAskingAgain()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var provider = new ScriptedProvider(clock, new HttpRequestException("throttled", inner: null, HttpStatusCode.TooManyRequests));
        using var client = new TransientProviderRetryChatClient(provider, clock, Unlimited);

        // Act
        await AdvanceUntilCompletedAsync(clock, AskAsync(client, TestContext.Current.CancellationToken));

        // Assert
        Assert.True(provider.AskedAt[1] > provider.AskedAt[0]);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task GetResponseAsync_ARefusedRequestOrRejectedCredential_FailsWithoutAskingAgain(HttpStatusCode status)
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var provider = new ScriptedProvider(clock, new HttpRequestException("refused", inner: null, status));
        using var client = new TransientProviderRetryChatClient(provider, clock, Unlimited);

        // Act
        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => AskAsync(client, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(status, failure.StatusCode);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task GetResponseAsync_AFailureNoProviderProduced_FailsWithoutAskingAgain()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var provider = new ScriptedProvider(clock, new InvalidOperationException("the answer could not be read"));
        using var client = new TransientProviderRetryChatClient(provider, clock, Unlimited);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => AskAsync(client, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(1, provider.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, TransientProviderRetryChatClient.RateLimitedMaxAttempts)]
    [InlineData(HttpStatusCode.ServiceUnavailable, TransientProviderRetryChatClient.MaxAttempts)]
    public async Task GetResponseAsync_TransientFailuresOnEveryAttempt_StopsAtTheBoundAndRethrowsTheLastFailure(HttpStatusCode status, int bound)
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var provider = new ScriptedProvider(clock, [.. Enumerable.Range(1, bound)
            .Select(attempt => new HttpRequestException($"busy on attempt {attempt}", inner: null, status))]);
        using var client = new TransientProviderRetryChatClient(provider, clock, Unlimited);

        // Act
        var failure = await Assert.ThrowsAsync<HttpRequestException>(
            () => AdvanceUntilCompletedAsync(clock, AskAsync(client, TestContext.Current.CancellationToken)));

        // Assert
        Assert.Equal($"busy on attempt {bound}", failure.Message);
        Assert.Equal(bound, provider.Calls);
    }

    [Fact]
    public async Task GetResponseAsync_ARateLimitNamingSecondsInRetryAfter_AsksAgainExactlyThatMuchLater()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var provider = new ScriptedProvider(clock, FakeProviderResponse.Refusal(429, ("Retry-After", "90")));
        using var client = new TransientProviderRetryChatClient(provider, clock, Unlimited);

        // Act
        await AdvanceUntilCompletedAsync(clock, AskAsync(client, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(90), provider.AskedAt[1] - provider.AskedAt[0]);
    }

    [Fact]
    public async Task GetResponseAsync_ARateLimitNamingADateInRetryAfter_AsksAgainAtThatDate()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var askAgainAt = clock.GetUtcNow().AddSeconds(75).ToString("R", CultureInfo.InvariantCulture);
        using var provider = new ScriptedProvider(clock, FakeProviderResponse.Refusal(429, ("Retry-After", askAgainAt)));
        using var client = new TransientProviderRetryChatClient(provider, clock, Unlimited);

        // Act
        await AdvanceUntilCompletedAsync(clock, AskAsync(client, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(75), provider.AskedAt[1] - provider.AskedAt[0]);
    }

    [Fact]
    public async Task GetResponseAsync_ARetryAfterBeyondTheLongestWait_WaitsOnlyTheLongestWait()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var provider = new ScriptedProvider(clock, FakeProviderResponse.Refusal(429, ("Retry-After", "86400")));
        using var client = new TransientProviderRetryChatClient(provider, clock, Unlimited);

        // Act
        await AdvanceUntilCompletedAsync(clock, AskAsync(client, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(TransientProviderRetryChatClient.LongestRequestedDelay, provider.AskedAt[1] - provider.AskedAt[0]);
    }

    [Fact]
    public async Task GetResponseAsync_ARateLimitNamingNoTime_WaitsLongerThanAnOverloadDoes()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var throttled = new ScriptedProvider(clock, new HttpRequestException("throttled", inner: null, HttpStatusCode.TooManyRequests));
        using var overloaded = new ScriptedProvider(clock, new HttpRequestException("overloaded", inner: null, HttpStatusCode.ServiceUnavailable));
        using var throttledClient = new TransientProviderRetryChatClient(throttled, clock, Unlimited);
        using var overloadedClient = new TransientProviderRetryChatClient(overloaded, clock, Unlimited);

        // Act
        await AdvanceUntilCompletedAsync(clock, AskAsync(throttledClient, TestContext.Current.CancellationToken));
        await AdvanceUntilCompletedAsync(clock, AskAsync(overloadedClient, TestContext.Current.CancellationToken));

        // Assert
        Assert.InRange(throttled.AskedAt[1] - throttled.AskedAt[0], TransientProviderRetryChatClient.RateLimitedBaseDelay / 2, TransientProviderRetryChatClient.RateLimitedMaxDelay);
        Assert.InRange(overloaded.AskedAt[1] - overloaded.AskedAt[0], TimeSpan.Zero, TransientProviderRetryChatClient.BaseDelay + Step);
    }

    [Fact]
    public async Task GetResponseAsync_MoreCallsThanTheRunHasSlotsFor_SendsTheNextOnlyOnceASlotIsFree()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var slots = new SemaphoreSlim(1);
        using var provider = new HeldProvider();
        using var first = new TransientProviderRetryChatClient(provider, clock, slots);
        using var second = new TransientProviderRetryChatClient(provider, clock, slots);

        // Act
        // CA2025 cannot see that both calls are awaited below, before the clients they use leave scope. Holding them
        // unawaited is the point of the test: it is what keeps the first call's slot taken while the second asks.
#pragma warning disable CA2025
        var firstCall = AskAsync(first, TestContext.Current.CancellationToken);
        var secondCall = AskAsync(second, TestContext.Current.CancellationToken);
#pragma warning restore CA2025
        var sentWhileTheFirstWasOut = provider.Sent;
        provider.Release();
        await firstCall;
        await secondCall;

        // Assert
        Assert.Equal((1, 2), (sentWhileTheFirstWasOut, provider.Sent));
    }

    [Fact]
    public async Task GetResponseAsync_TheCallerCancels_FailsWithoutAskingAgain()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource();
        using var provider = new HeldProvider();
        using var client = new TransientProviderRetryChatClient(provider, clock, Unlimited);
        // CA2025 cannot see that the call is awaited below, before the client it uses leaves scope. Holding it unawaited
        // is the point of the test: it is what lets the caller cancel while the provider still holds the call.
#pragma warning disable CA2025
        var call = AskAsync(client, cancellation.Token);
#pragma warning restore CA2025

        // Act
        await cancellation.CancelAsync();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Assert.Equal(1, provider.Sent);
    }

    private static Task<ChatResponse> AskAsync(TransientProviderRetryChatClient client, CancellationToken cancellationToken) =>
        client.GetResponseAsync([new ChatMessage(ChatRole.User, "Which build fixed the export?")], cancellationToken: cancellationToken);

    /// <summary>Moves the clock on, a second at a time, until the call has finished waiting between its attempts.</summary>
    private static async Task<ChatResponse> AdvanceUntilCompletedAsync(FakeTimeProvider clock, Task<ChatResponse> call)
    {
        for (var step = 0; step < 3600 && !call.IsCompleted; step++)
        {
            clock.Advance(Step);

            await Task.Yield();
        }

        Assert.True(call.IsCompleted, "The call was still waiting after the clock had moved past every delay it could draw.");

        return await call;
    }

    /// <summary>A provider that fails with each scripted failure in turn and answers once they are spent.</summary>
    private sealed class ScriptedProvider(TimeProvider clock, params Exception[] failures) : IChatClient
    {
        public List<DateTimeOffset> AskedAt { get; } = [];

        public int Calls => this.AskedAt.Count;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            this.AskedAt.Add(clock.GetUtcNow());

            return this.Calls <= failures.Length
                ? Task.FromException<ChatResponse>(failures[this.Calls - 1])
                : Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Answer)));
        }

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

    /// <summary>A provider whose every answer is held until the test releases them all, counting the calls sent to it meanwhile.</summary>
    private sealed class HeldProvider : IChatClient
    {
        private readonly TaskCompletionSource answered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int sent;

        public int Sent => Volatile.Read(ref this.sent);

        public void Release() => this.answered.SetResult();

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref this.sent);
            await this.answered.Task.WaitAsync(cancellationToken);

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, Answer));
        }

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
