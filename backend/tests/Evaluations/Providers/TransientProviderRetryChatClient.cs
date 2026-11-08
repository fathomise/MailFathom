// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ClientModel;
using System.Net.Http.Headers;
using MailFathom.AI.ProviderAdapters;
using MailFathom.Application.Resilience;
using Microsoft.Extensions.AI;

namespace MailFathom.Evaluations.Providers;

/// <summary>Asks a provider again when a call failed because the provider was busy or unreachable, and never otherwise.</summary>
/// <remarks>
/// <para>
/// The call is the unit retried rather than the test, because only the call knows why it failed. A rate limit, a request
/// that timed out, and a transport fault say nothing about the model, so each is asked again after a delay, as
/// <see cref="ProviderCallFailureClassification" /> sorts them for a deployment. A refused request and a rejected
/// credential would be refused again, and an answer that falls short is not a failure of the call at all, so both reach
/// the scenario at once and a shortfall is paid for once.
/// </para>
/// <para>
/// A failure whose response says when to ask again, in <c>Retry-After</c> as seconds or as a date, is asked again then,
/// up to <see cref="LongestRequestedDelay" />. A rate limit that says nothing is given longer and more attempts than an
/// overload or a transport fault, because an account's limit is counted over a window that three quick attempts do not
/// outlast; the others are asked again after a short jittered delay.
/// </para>
/// <para>
/// Every attempt takes a slot of <paramref name="callsInFlight" /> while it is sent, and none while it waits to be asked
/// again, so the run's limit on calls in flight holds however many clients it opened.
/// </para>
/// <para>
/// A request abandoned at the transport's own timeout arrives as a cancellation the caller never asked for, which the
/// classifier leaves unclassified; it is read as a timeout here, the way a deployment's resilient client reads it. The
/// caller's own cancellation is never retried.
/// </para>
/// <para>
/// Only the call that returns one answer is retried: nothing in the suite streams, so a streamed call passes through
/// unchanged.
/// </para>
/// </remarks>
/// <param name="innerClient">The provider's client every attempt is sent through.</param>
/// <param name="timeProvider">The clock the delay between attempts is measured on.</param>
/// <param name="callsInFlight">The slots every attempt is sent under, shared by every client of the run.</param>
internal sealed class TransientProviderRetryChatClient(
    IChatClient innerClient,
    TimeProvider timeProvider,
    SemaphoreSlim callsInFlight)
    : DelegatingChatClient(innerClient)
{
    /// <summary>How many times one call failing on an overload, a timeout, or a transport fault is sent before its failure is reported.</summary>
    public const int MaxAttempts = 3;

    /// <summary>How many times one rate-limited call is sent before its failure is reported.</summary>
    public const int RateLimitedMaxAttempts = 5;

    /// <summary>The header a provider names the time to ask again in.</summary>
    private const string RetryAfterHeader = "Retry-After";

    /// <summary>The delay the first retry after an overload, a timeout, or a transport fault is drawn around.</summary>
    internal static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(5);

    /// <summary>The longest any one wait after an overload, a timeout, or a transport fault may grow to.</summary>
    internal static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

    /// <summary>The delay the first retry after a rate limit that named no time is drawn around.</summary>
    internal static readonly TimeSpan RateLimitedBaseDelay = TimeSpan.FromSeconds(20);

    /// <summary>The longest any one wait after a rate limit that named no time may grow to.</summary>
    internal static readonly TimeSpan RateLimitedMaxDelay = TimeSpan.FromMinutes(2);

    /// <summary>The longest wait a provider's <c>Retry-After</c> is honoured for, so one answer cannot stall a run.</summary>
    internal static readonly TimeSpan LongestRequestedDelay = TimeSpan.FromMinutes(5);

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Materialized once, because a sequence the caller built is not promised to survive a second enumeration.
        var conversation = messages as IReadOnlyList<ChatMessage> ?? [.. messages];

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await this.SendAsync(conversation, options, cancellationToken);
            }
            catch (Exception failure) when (ClassifyTransient(failure, cancellationToken) is { } transient
                && attempt < AttemptsFor(transient))
            {
                await Task.Delay(
                    DelayBeforeRetry(transient, this.RequestedDelayOf(failure), attempt),
                    timeProvider,
                    cancellationToken);
            }
        }
    }

    /// <summary>Gets how many times a call failing this way is sent in all.</summary>
    /// <param name="failure">How the call failed.</param>
    /// <returns>The bound on attempts.</returns>
    internal static int AttemptsFor(ProviderCallFailure failure) =>
        failure is ProviderCallFailure.RateLimited ? RateLimitedMaxAttempts : MaxAttempts;

    /// <summary>Computes how long a call failing this way waits before it is asked again.</summary>
    /// <param name="failure">How the call failed.</param>
    /// <param name="requested">How long the provider asked to be left, or <see langword="null" /> where it named no time.</param>
    /// <param name="attempt">How many attempts the call has made, counting from one.</param>
    /// <returns>The provider's own time up to <see cref="LongestRequestedDelay" />, or a jittered delay that grows with the attempts.</returns>
    internal static TimeSpan DelayBeforeRetry(ProviderCallFailure failure, TimeSpan? requested, int attempt) =>
        (failure, requested) switch
        {
            (_, { } asked) => asked < TimeSpan.Zero ? TimeSpan.Zero : asked > LongestRequestedDelay ? LongestRequestedDelay : asked,
            (ProviderCallFailure.RateLimited, null) =>
                JitteredRetryBackoff.DelayBeforeNextAttempt(RateLimitedBaseDelay, RateLimitedMaxDelay, minimumDelay: TimeSpan.Zero, attempt),
            _ => JitteredRetryBackoff.DelayBeforeNextAttempt(BaseDelay, MaxDelay, minimumDelay: TimeSpan.Zero, attempt),
        };

    /// <summary>Sorts a failure the provider would likely not repeat, and leaves every other one unclassified.</summary>
    /// <param name="failure">The failure a call ended in.</param>
    /// <param name="cancellationToken">The caller's token, whose cancellation is never retried.</param>
    /// <returns>The classification of a rate limit, a timeout, or a transport fault, and <see langword="null" /> for anything else.</returns>
    internal static ProviderCallFailure? ClassifyTransient(Exception failure, CancellationToken cancellationToken)
    {
        if (failure is OperationCanceledException)
        {
            return cancellationToken.IsCancellationRequested ? null : ProviderCallFailure.RequestTimedOut;
        }

        var classified = ProviderCallFailureClassification.Classify(failure);

        return classified is ProviderCallFailure.RateLimited or ProviderCallFailure.RequestTimedOut or ProviderCallFailure.TransportFaulted
            ? classified
            : null;
    }

    private async Task<ChatResponse> SendAsync(
        IReadOnlyList<ChatMessage> conversation,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        await callsInFlight.WaitAsync(cancellationToken);

        try
        {
            return await base.GetResponseAsync(conversation, options, cancellationToken);
        }
        finally
        {
            callsInFlight.Release();
        }
    }

    /// <summary>Reads how long the provider asked to be left before the next attempt, from the response a refusal carries.</summary>
    private TimeSpan? RequestedDelayOf(Exception failure)
    {
        if (failure is not ClientResultException refusal
            || refusal.GetRawResponse() is not { } response
            || !response.Headers.TryGetValue(RetryAfterHeader, out var value)
            || !RetryConditionHeaderValue.TryParse(value, out var retryAfter))
        {
            return null;
        }

        return retryAfter.Delta ?? retryAfter.Date - timeProvider.GetUtcNow();
    }
}
