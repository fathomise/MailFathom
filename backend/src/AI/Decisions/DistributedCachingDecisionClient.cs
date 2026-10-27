// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;

namespace MailFathom.AI.Decisions;

/// <summary>Answers a request it has seen before from a cache, and files every answer it had to ask for.</summary>
/// <remarks>
/// <para>
/// Shaped after <see cref="DistributedCachingChatClient" />, and for the same use: an evaluation run reads a filed
/// answer back instead of paying for it again. Production composes no cache — a decision is about one message, and two
/// messages are never the same request.
/// </para>
/// <para>
/// The key covers everything that could change the answer: the input, every question with its options, the request
/// options, and whatever the composition adds through <see cref="CacheKeyAdditionalValues" /> — the model above all.
/// A changed question is therefore a different key and is asked again, rather than read back under an answer given to
/// the question it replaced.
/// </para>
/// <para>
/// Designed for inheritance, as the chat original is: an evaluation run overrides the read and the write to count where
/// each answer came from.
/// </para>
/// </remarks>
[SuppressMessage(
    "Performance",
    "CA1852:Seal internal types",
    Justification = "Inherited by the evaluation suite's tallying cache, which reaches it through InternalsVisibleTo, so it has a subtype the analyzer cannot see.")]
internal class DistributedCachingDecisionClient : DelegatingDecisionClient
{
    private readonly IDistributedCache storage;

    /// <summary>Initializes the middleware over the client it asks and the cache it files answers in.</summary>
    /// <param name="innerClient">The client asked when the cache has no answer.</param>
    /// <param name="storage">Where answers are filed.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public DistributedCachingDecisionClient(IDecisionClient innerClient, IDistributedCache storage)
        : base(innerClient)
    {
        ArgumentNullException.ThrowIfNull(storage);

        this.storage = storage;
    }

    /// <summary>Gets or sets values the key is made of beside the request, such as the model and the address it is asked at.</summary>
    public IReadOnlyList<string>? CacheKeyAdditionalValues { get; set; }

    /// <inheritdoc />
    public override async Task<DecisionResponse> DecideAsync(
        IEnumerable<ChatMessage> input,
        IReadOnlyList<DecisionQuestion> questions,
        DecisionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        DecisionQuestion.RequireUsable(questions);

        IReadOnlyList<ChatMessage> evidence = [.. input];
        var key = this.GetCacheKey(evidence, questions, options);

        if (await this.ReadCacheAsync(key, cancellationToken) is { } cached)
        {
            return cached;
        }

        var response = await base.DecideAsync(evidence, questions, options, cancellationToken);

        await this.WriteCacheAsync(key, response, cancellationToken);

        return response;
    }

    /// <summary>Names the cache entry a request is filed under.</summary>
    /// <param name="input">The request's input.</param>
    /// <param name="questions">The request's questions.</param>
    /// <param name="options">The request's options.</param>
    /// <returns>A hash of the canonical serialization of all of them and of <see cref="CacheKeyAdditionalValues" />.</returns>
    protected virtual string GetCacheKey(
        IReadOnlyList<ChatMessage> input,
        IReadOnlyList<DecisionQuestion> questions,
        DecisionOptions? options)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(questions);

        using var canonical = new MemoryStream();
        using (var writer = new Utf8JsonWriter(canonical))
        {
            writer.WriteStartArray();
            JsonSerializer.Serialize(writer, (IList<ChatMessage>)[.. input], DecisionJsonContext.TypeInfoFor<IList<ChatMessage>>());
            JsonSerializer.Serialize(writer, questions, DecisionJsonContext.TypeInfoFor<IReadOnlyList<DecisionQuestion>>());
            JsonSerializer.Serialize(writer, options, DecisionJsonContext.TypeInfoFor<DecisionOptions?>());

            foreach (var value in this.CacheKeyAdditionalValues ?? [])
            {
                writer.WriteStringValue(value);
            }

            writer.WriteEndArray();
        }

        return Convert.ToHexString(SHA256.HashData(canonical.ToArray()));
    }

    /// <summary>Reads the answer filed under a key.</summary>
    /// <param name="key">The entry's name.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>The filed answer, or <see langword="null" /> when nothing is filed under the key.</returns>
    protected virtual async Task<DecisionResponse?> ReadCacheAsync(string key, CancellationToken cancellationToken)
    {
        var filed = await this.storage.GetAsync(key, cancellationToken);

        return filed is null
            ? null
            : JsonSerializer.Deserialize(filed, DecisionJsonContext.TypeInfoFor<DecisionResponse>());
    }

    /// <summary>Files an answer under a key.</summary>
    /// <param name="key">The entry's name.</param>
    /// <param name="value">The answer.</param>
    /// <param name="cancellationToken">Stops the write.</param>
    /// <returns>A task completing once the answer is filed.</returns>
    protected virtual Task WriteCacheAsync(string key, DecisionResponse value, CancellationToken cancellationToken) =>
        this.storage.SetAsync(
            key,
            JsonSerializer.SerializeToUtf8Bytes(value, DecisionJsonContext.TypeInfoFor<DecisionResponse>()),
            new DistributedCacheEntryOptions(),
            cancellationToken);
}
