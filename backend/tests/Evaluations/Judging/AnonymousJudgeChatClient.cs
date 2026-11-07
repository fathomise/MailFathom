// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using Microsoft.Extensions.AI;

namespace MailFathom.Evaluations.Judging;

/// <summary>Presents a judge under a fixed name and its role on the panel, so nothing the evaluation libraries record can say which model it was.</summary>
/// <remarks>
/// <para>
/// The libraries record a verdict's model from three places, and this closes all three: the model a response names, which
/// becomes the turn details and the <c>eval-model</c> metadata of every metric; the metadata the client publishes, whose
/// provider address becomes the turn's provider; and a failure, whose message the composite evaluator stores as a
/// diagnostic and which a provider fills with the model it could not reach.
/// </para>
/// <para>
/// It sits beneath the response cache rather than above it, so what the cache stores is already anonymous — the cache is
/// kept between runs exactly as the results are.
/// </para>
/// <para>
/// The role is what lets a reader of the turn details tell which judge of the panel answered a call, and it names a seat
/// rather than a model.
/// </para>
/// </remarks>
/// <param name="judge">The judge's own client, which this takes ownership of.</param>
/// <param name="role">The role the judge holds on the panel, such as <c>juror 1</c> or <c>arbiter</c>.</param>
internal sealed class AnonymousJudgeChatClient(IChatClient judge, string role) : DelegatingChatClient(judge)
{
    /// <summary>The name every judge is recorded under, beside its role.</summary>
    public const string Name = "judge";

    private readonly string recordedModel = $"{Name} ({role})";

    private static readonly ChatClientMetadata AnonymousMetadata = new(providerName: Name);

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ChatResponse response;

        try
        {
            response = await base.GetResponseAsync(messages, options, cancellationToken);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // The original is deliberately not kept as the inner exception: its message and every inner one are where a
            // provider names the model and the address, and whatever holds this exception prints the whole chain.
            throw new InvalidOperationException($"The {this.recordedModel} did not answer ({failure.GetType().Name}).");
        }

        response.ModelId = this.recordedModel;
        response.AdditionalProperties = null;

        return response;
    }

    /// <inheritdoc />
    /// <remarks>Refused, because every evaluator asks for one answer and a stream would be a second path to anonymize.</remarks>
    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The judge is asked for one answer at a time.");

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType == typeof(ChatClientMetadata)
            ? AnonymousMetadata
            : base.GetService(serviceType, serviceKey);
}
