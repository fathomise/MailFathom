// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using Microsoft.Extensions.AI;

namespace MailFathom.AI.Decisions;

/// <summary>Composes a decision client from the client that reaches a provider and the middleware around it.</summary>
/// <remarks>
/// Shaped after <see cref="ChatClientBuilder" />, and composed in the same order: the first middleware added is the
/// outermost, so a call meets the middleware in the order they were written and the adapter last.
/// </remarks>
internal sealed class DecisionClientBuilder
{
    private readonly Func<IServiceProvider, IDecisionClient> innerClientFactory;
    private readonly List<Func<IDecisionClient, IServiceProvider, IDecisionClient>> middleware = [];

    /// <summary>Initializes a builder over the client that reaches the provider.</summary>
    /// <param name="innerClient">The innermost client.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="innerClient" /> is <see langword="null" />.</exception>
    public DecisionClientBuilder(IDecisionClient innerClient)
    {
        ArgumentNullException.ThrowIfNull(innerClient);

        this.innerClientFactory = _ => innerClient;
    }

    /// <summary>Initializes a builder over a factory for the client that reaches the provider.</summary>
    /// <param name="innerClientFactory">Creates the innermost client from the services it is built with.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="innerClientFactory" /> is <see langword="null" />.</exception>
    public DecisionClientBuilder(Func<IServiceProvider, IDecisionClient> innerClientFactory)
    {
        ArgumentNullException.ThrowIfNull(innerClientFactory);

        this.innerClientFactory = innerClientFactory;
    }

    /// <summary>Adds a middleware inside every one already added.</summary>
    /// <param name="clientFactory">Wraps the client beneath it.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="clientFactory" /> is <see langword="null" />.</exception>
    public DecisionClientBuilder Use(Func<IDecisionClient, IDecisionClient> clientFactory)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);

        return this.Use((innerClient, _) => clientFactory(innerClient));
    }

    /// <summary>Adds a middleware inside every one already added, built from the services the client is built with.</summary>
    /// <param name="clientFactory">Wraps the client beneath it.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="clientFactory" /> is <see langword="null" />.</exception>
    public DecisionClientBuilder Use(Func<IDecisionClient, IServiceProvider, IDecisionClient> clientFactory)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);

        this.middleware.Add(clientFactory);

        return this;
    }

    /// <summary>Builds the composed client.</summary>
    /// <param name="services">The services each factory is handed, or <see langword="null" /> for an empty set.</param>
    /// <returns>The outermost client.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a factory returns <see langword="null" />.</exception>
    public IDecisionClient Build(IServiceProvider? services = null)
    {
        services ??= EmptyServiceProvider.Instance;

        var innerClient = this.innerClientFactory(services)
            ?? throw new InvalidOperationException("The factory for the innermost decision client returned null.");

        return Enumerable.Reverse(this.middleware).Aggregate(
            innerClient,
            (client, wrap) => wrap(client, services)
                ?? throw new InvalidOperationException("A decision middleware factory returned null."));
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static EmptyServiceProvider Instance { get; } = new();

        public object? GetService(Type serviceType) => null;
    }
}
