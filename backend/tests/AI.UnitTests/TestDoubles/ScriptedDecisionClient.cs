// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.Decisions;
using Microsoft.Extensions.AI;

namespace MailFathom.AI.UnitTests.TestDoubles;

/// <summary>A decision client that answers every question it is asked from a script, and records each call.</summary>
/// <remarks>
/// Answers are produced per call rather than queued, because a middleware test asks the same thing several times and a
/// script that ran out would fail the test for a reason unrelated to the middleware.
/// </remarks>
internal sealed class ScriptedDecisionClient : IDecisionClient
{
    private readonly Func<IReadOnlyList<DecisionQuestion>, CancellationToken, Task<DecisionResponse>> answer;
    private readonly List<DecisionCall> calls = [];

    private ScriptedDecisionClient(Func<IReadOnlyList<DecisionQuestion>, CancellationToken, Task<DecisionResponse>> answer) =>
        this.answer = answer;

    /// <summary>Gets what each call was asked, in order.</summary>
    public IReadOnlyList<DecisionCall> Calls => this.calls;

    /// <summary>Gets whether the client was disposed.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>Builds a client answering every question with its first option, certain of it, and reporting the given usage.</summary>
    /// <param name="inputTokens">The tokens the provider reports the input occupied, or <see langword="null" /> to report no usage.</param>
    /// <param name="outputTokens">The tokens the provider reports the answer occupied.</param>
    /// <param name="modelId">The build the provider reports answered.</param>
    public static ScriptedDecisionClient AnsweringFirstOption(
        long? inputTokens = null,
        long outputTokens = 0,
        string modelId = "vendor/decider-2026-10-01") =>
        new((questions, _) => Task.FromResult(
            new DecisionResponse([.. questions.Select(FirstOptionOf)])
            {
                ModelId = modelId,
                Usage = inputTokens is { } input
                    ? new UsageDetails { InputTokenCount = input, OutputTokenCount = outputTokens }
                    : null,
            }));

    /// <summary>Builds a client that fails every call the way the given exception says.</summary>
    /// <param name="failure">What every call raises.</param>
    public static ScriptedDecisionClient Throwing(Exception failure) => new((_, _) => throw failure);

    /// <summary>Builds a client that never answers, so whatever deadline its caller holds is what ends the call.</summary>
    public static ScriptedDecisionClient Stalling() => new(async (_, cancellationToken) =>
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        throw new InvalidOperationException("The stall was never meant to end.");
    });

    /// <inheritdoc />
    public Task<DecisionResponse> DecideAsync(
        IEnumerable<ChatMessage> input,
        IReadOnlyList<DecisionQuestion> questions,
        DecisionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        this.calls.Add(new DecisionCall([.. input], questions, options));

        return this.answer(questions, cancellationToken);
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    /// <inheritdoc />
    public void Dispose() => this.IsDisposed = true;

    private static DecisionAnswer FirstOptionOf(DecisionQuestion question) => new(
        question.Id,
        question.Options[0].Value,
        question.Options.ToDictionary(option => option.Value, option => option == question.Options[0] ? 1d : 0d));

    /// <summary>What one call was asked.</summary>
    /// <param name="Input">The input the call decided about.</param>
    /// <param name="Questions">The questions it asked.</param>
    /// <param name="Options">The options it carried.</param>
    internal sealed record DecisionCall(
        IReadOnlyList<ChatMessage> Input,
        IReadOnlyList<DecisionQuestion> Questions,
        DecisionOptions? Options);
}
