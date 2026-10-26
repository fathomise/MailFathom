// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using Microsoft.Extensions.AI;

namespace MailFathom.AI.Decisions;

/// <summary>Asks a model closed questions about some input and returns a probability for every option it was offered.</summary>
/// <remarks>
/// <para>
/// Shaped after <see cref="IChatClient" /> so that everything a deployment wraps around a model call wraps a decision
/// the same way: a client is composed from an adapter and middleware through <see cref="DecisionClientBuilder" />, every
/// middleware derives from <see cref="DelegatingDecisionClient" />, and <see cref="GetService" /> reaches any layer of
/// the composition from its outside.
/// </para>
/// <para>
/// The types under this namespace name no provider and carry no MailFathom domain type, so they can be swapped for an
/// official abstraction once Microsoft.Extensions.AI or the OpenAI .NET library publishes one, and only the adapter
/// beneath them would move.
/// </para>
/// </remarks>
internal interface IDecisionClient : IDisposable
{
    /// <summary>Asks every question about the input in one request.</summary>
    /// <param name="input">The evidence the questions are about: text, and JSON object data whose members name what they hold.</param>
    /// <param name="questions">The questions, each under an identifier unique within the request.</param>
    /// <param name="options">What the request is sent with, or <see langword="null" /> for the client's defaults.</param>
    /// <param name="cancellationToken">Stops the request.</param>
    /// <returns>One answer per question, in the order the questions were given.</returns>
    /// <exception cref="ArgumentException">Thrown when the questions are empty, repeat an identifier, or the input carries content the client cannot send.</exception>
    /// <remarks>
    /// The questions are answered independently of each other. Probabilities from two questions are therefore not
    /// comparable, and a choice the caller needs to be consistent across several outcomes belongs in one question with
    /// one option per outcome.
    /// </remarks>
    Task<DecisionResponse> DecideAsync(
        IEnumerable<ChatMessage> input,
        IReadOnlyList<DecisionQuestion> questions,
        DecisionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Finds an object of the requested type in this client or in any client it wraps.</summary>
    /// <param name="serviceType">The type wanted.</param>
    /// <param name="serviceKey">An optional key narrowing which object is wanted.</param>
    /// <returns>The object, or <see langword="null" /> when no layer holds one.</returns>
    /// <remarks>Asking for <see cref="DecisionClientMetadata" /> is how a caller learns which provider and model a composition reaches.</remarks>
    object? GetService(Type serviceType, object? serviceKey = null);
}
