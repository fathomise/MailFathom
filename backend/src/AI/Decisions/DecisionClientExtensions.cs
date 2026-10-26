// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace MailFathom.AI.Decisions;

/// <summary>What every decision client offers without implementing it itself.</summary>
internal static class DecisionClientExtensions
{
    /// <summary>Finds an object of the requested type in the client or in any client it wraps.</summary>
    /// <typeparam name="TService">The type wanted.</typeparam>
    /// <param name="client">The client to search.</param>
    /// <param name="serviceKey">An optional key narrowing which object is wanted.</param>
    /// <returns>The object, or <see langword="null" /> when no layer holds one.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="client" /> is <see langword="null" />.</exception>
    public static TService? GetService<TService>(this IDecisionClient client, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        return client.GetService(typeof(TService), serviceKey) is TService service ? service : default;
    }

    /// <summary>Offers a fixed set of closed questions to an agent as one of its tools.</summary>
    /// <param name="client">The client the questions are asked through.</param>
    /// <param name="name">The tool's name, as the agent calls it.</param>
    /// <param name="description">What the tool tells the agent it is for.</param>
    /// <param name="questions">The questions every call asks about the text the agent passes.</param>
    /// <param name="options">What every call is sent with, or <see langword="null" /> for the client's defaults.</param>
    /// <returns>A function taking the text to decide about and returning one answer per question.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="client" /> or <paramref name="questions" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the name or the description is blank, or the questions could not be asked.</exception>
    /// <remarks>
    /// The questions are fixed when the tool is made rather than written by the agent, which keeps the property a closed
    /// question exists for: the agent chooses what to ask about and never what the answers may be.
    /// </remarks>
    public static AIFunction AsAIFunction(
        this IDecisionClient client,
        string name,
        string description,
        IReadOnlyList<DecisionQuestion> questions,
        DecisionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        DecisionQuestion.RequireUsable(questions);

        IReadOnlyList<DecisionQuestion> asked = [.. questions];

        return AIFunctionFactory.Create(
            async ([Description("The text the questions are about.")] string input, CancellationToken cancellationToken) =>
            {
                var response = await client.DecideAsync(
                    [new ChatMessage(ChatRole.User, input)],
                    asked,
                    options,
                    cancellationToken);

                return response.Answers;
            },
            name,
            description,
            DecisionJsonContext.SerializerOptions);
    }
}
