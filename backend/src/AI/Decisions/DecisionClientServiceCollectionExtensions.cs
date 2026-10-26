// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using Microsoft.Extensions.DependencyInjection;

namespace MailFathom.AI.Decisions;

/// <summary>Registers a composed decision client, in the style of Microsoft.Extensions.AI's <c>AddChatClient</c>.</summary>
internal static class DecisionClientServiceCollectionExtensions
{
    /// <summary>Registers a decision client and returns the builder its middleware is added to.</summary>
    /// <param name="services">The collection to register in.</param>
    /// <param name="innerClientFactory">Creates the client that reaches the provider.</param>
    /// <param name="lifetime">How long one composed client lives.</param>
    /// <returns>The builder the registration builds from when the client is resolved.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public static DecisionClientBuilder AddDecisionClient(
        this IServiceCollection services,
        Func<IServiceProvider, IDecisionClient> innerClientFactory,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(innerClientFactory);

        var builder = new DecisionClientBuilder(innerClientFactory);

        services.Add(new ServiceDescriptor(typeof(IDecisionClient), provider => builder.Build(provider), lifetime));

        return builder;
    }

    /// <summary>Registers a decision client under a key and returns the builder its middleware is added to.</summary>
    /// <param name="services">The collection to register in.</param>
    /// <param name="serviceKey">The key the client is resolved by.</param>
    /// <param name="innerClientFactory">Creates the client that reaches the provider.</param>
    /// <param name="lifetime">How long one composed client lives.</param>
    /// <returns>The builder the registration builds from when the client is resolved.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public static DecisionClientBuilder AddKeyedDecisionClient(
        this IServiceCollection services,
        object? serviceKey,
        Func<IServiceProvider, IDecisionClient> innerClientFactory,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(innerClientFactory);

        var builder = new DecisionClientBuilder(innerClientFactory);

        services.Add(new ServiceDescriptor(
            typeof(IDecisionClient),
            serviceKey,
            (provider, _) => builder.Build(provider),
            lifetime));

        return builder;
    }
}
