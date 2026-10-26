// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using Microsoft.Extensions.AI;

namespace MailFathom.AI.Decisions;

/// <summary>The base every decision middleware derives from: it passes each call to the client it wraps unless it overrides it.</summary>
/// <remarks>Shaped after <see cref="DelegatingChatClient" />, including that it owns the client it wraps and disposes it.</remarks>
internal abstract class DelegatingDecisionClient : IDecisionClient
{
    /// <summary>Initializes the middleware over the client it wraps.</summary>
    /// <param name="innerClient">The client every call is passed to.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="innerClient" /> is <see langword="null" />.</exception>
    protected DelegatingDecisionClient(IDecisionClient innerClient)
    {
        ArgumentNullException.ThrowIfNull(innerClient);

        this.InnerClient = innerClient;
    }

    /// <summary>Gets the client every call is passed to.</summary>
    protected IDecisionClient InnerClient { get; }

    /// <inheritdoc />
    public virtual Task<DecisionResponse> DecideAsync(
        IEnumerable<ChatMessage> input,
        IReadOnlyList<DecisionQuestion> questions,
        DecisionOptions? options = null,
        CancellationToken cancellationToken = default) =>
        this.InnerClient.DecideAsync(input, questions, options, cancellationToken);

    /// <inheritdoc />
    /// <remarks>Answers with this middleware itself when it is what was asked for, and asks the client it wraps otherwise.</remarks>
    public virtual object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return serviceKey is null && serviceType.IsInstanceOfType(this)
            ? this
            : this.InnerClient.GetService(serviceType, serviceKey);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the client this middleware wraps.</summary>
    /// <param name="disposing"><see langword="true" /> when called from <see cref="Dispose()" />.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.InnerClient.Dispose();
        }
    }
}
