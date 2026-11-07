// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using Microsoft.Extensions.AI;

namespace MailFathom.Evaluations.Judging;

/// <summary>The one client a run's evaluators are configured with, which hands each verdict to the judge the request names.</summary>
/// <remarks>
/// <para>
/// The evaluation libraries configure a run with one judge and cache its verdicts in front of it, so the panel stands
/// behind the cache as that one judge. <see cref="JudgePanelEvaluator" /> names a judge on each request through
/// <see cref="Asking" />, which puts the judge's <see cref="JudgeDeclaration.CachingKey" /> among the request's additional
/// properties. The cache keys a verdict on the request, that member included, so each judge's verdicts are filed apart
/// under its own identity, and this client takes the member off before the request leaves for the provider.
/// </para>
/// <para>
/// A request naming no judge of this panel is refused rather than sent to a default one, so nothing reaches a provider
/// without the panel's rule having chosen who answers it.
/// </para>
/// </remarks>
internal sealed class JudgePanelChatClient : IChatClient
{
    private const string JudgeMember = "mailfathom.judge";

    private static readonly ChatClientMetadata AnonymousMetadata = new(providerName: AnonymousJudgeChatClient.Name);

    private readonly Dictionary<string, IChatClient> judges;

    /// <summary>Initializes a new instance of the <see cref="JudgePanelChatClient" /> class.</summary>
    /// <param name="panel">The panel the judges sit on.</param>
    /// <param name="judges">Each judge's client under its caching key, which this takes ownership of.</param>
    public JudgePanelChatClient(JudgePanel panel, IEnumerable<(string CachingKey, IChatClient Client)> judges)
    {
        this.Panel = panel;
        this.judges = judges.ToDictionary(static judge => judge.CachingKey, static judge => judge.Client, StringComparer.Ordinal);
    }

    /// <summary>Gets the panel whose judges this client reaches.</summary>
    public JudgePanel Panel { get; }

    /// <summary>Wraps a client in front of the panel so every request it sends names one judge.</summary>
    /// <param name="panel">The client in front of the panel, usually the run's cache over it, which stays the caller's.</param>
    /// <param name="judge">The judge every request is for.</param>
    /// <returns>The client the evaluator asks through.</returns>
    public static IChatClient Asking(IChatClient panel, JudgeDeclaration judge) =>
        new JudgeNamingChatClient(panel, judge.CachingKey);

    /// <inheritdoc />
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (options?.AdditionalProperties?.TryGetValue(JudgeMember, out var named) is not true
            || named is not string cachingKey
            || !this.judges.TryGetValue(cachingKey, out var judge))
        {
            throw new InvalidOperationException("A verdict was asked for without naming which judge of the panel gives it.");
        }

        var forwarded = options.Clone();
        forwarded.AdditionalProperties = new(options.AdditionalProperties.Where(static member => member.Key != JudgeMember).ToDictionary());

        return judge.GetResponseAsync(messages, forwarded, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>Refused, because every evaluator asks for one answer and a stream would be a second path to route.</remarks>
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The judge is asked for one answer at a time.");

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is not null ? null
        : serviceType == typeof(ChatClientMetadata) ? AnonymousMetadata
        : serviceType.IsInstanceOfType(this) ? this
        : null;

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var judge in this.judges.Values)
        {
            judge.Dispose();
        }
    }

    /// <summary>Names one judge on every request, and owns nothing: the client in front of the panel belongs to the run that configured it.</summary>
    private sealed class JudgeNamingChatClient(IChatClient panel, string cachingKey) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var named = options?.Clone() ?? new ChatOptions();
            named.AdditionalProperties = new(options?.AdditionalProperties ?? new AdditionalPropertiesDictionary())
            {
                [JudgeMember] = cachingKey,
            };

            return panel.GetResponseAsync(messages, named, cancellationToken);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The judge is asked for one answer at a time.");

        public object? GetService(Type serviceType, object? serviceKey = null) => panel.GetService(serviceType, serviceKey);

        public void Dispose()
        {
        }
    }
}
