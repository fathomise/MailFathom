// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Host.Configuration.Chat;
using MailFathom.Host.Configuration.Decisions;
using MailFathom.Host.Configuration.Embeddings;

namespace MailFathom.Host.Configuration.Providers;

/// <summary>Holds the one rule the AI declarations share: an alias names one endpoint across the whole deployment.</summary>
/// <remarks>
/// <para>
/// Three sections declare an endpoint — embedding, chat, and decision — and none of them can enforce this on its own,
/// because each validates what it declares and cannot see the others. The two checks against a chat alias live here
/// rather than inline in the composition root so they are reachable from a test: they are what keeps a chat outage from
/// opening the circuit the embeddings or a decision model are served through, and what keeps a chat key from being
/// presented at a decision model's address. A comparison written the wrong way round — or one that quietly loses its
/// case-insensitivity — would leave both true while looking correct.
/// </para>
/// <para>
/// Both are judged from the chat side, because the chat section is the one that reloads and so the one a collision can
/// arrive with. Duplicate aliases *within* a section are refused by <see cref="EmbeddingOptions" />,
/// <see cref="ChatModelOptions" />, and <see cref="DecisionModelOptions" /> themselves, and a decision alias reused by
/// an embedding endpoint — two sections neither of which reloads — is refused by
/// <see cref="DecisionModelOptions.FindDeclarationErrors" />.
/// </para>
/// </remarks>
internal static class ProviderEndpointAliases
{
    /// <summary>Finds the embedding endpoint alias a declared chat model reuses, if any.</summary>
    /// <param name="embeddings">The bound embedding declaration, or <see langword="null" /> when the deployment wrote no section.</param>
    /// <param name="chat">The bound chat declaration, or <see langword="null" /> when the deployment wrote no section.</param>
    /// <returns>The reused alias as the embedding chain spells it, or <see langword="null" /> when nothing collides.</returns>
    /// <remarks>
    /// Compared without case, and after trimming both sides, because that is how the alias is matched everywhere else it
    /// is used — a credential resolved by it would otherwise reach one endpoint while a log line named the other. Every
    /// declared chat model is read rather than only the one that answers questions, because each is an endpoint of its
    /// own with its own credential and its own circuit.
    /// </remarks>
    public static string? FindReusedAlias(EmbeddingOptions? embeddings, ChatModelOptions? chat)
    {
        if (chat is null || embeddings is null)
        {
            return null;
        }

        var chatAliases = chat.DeclaredAliases;

        return embeddings.Endpoints
            .Select(endpoint => endpoint.Alias.Trim())
            .FirstOrDefault(alias => chatAliases.Contains(alias, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Describes the collision for an operator, naming the alias and what it costs to leave it.</summary>
    /// <param name="reusedAlias">The alias both sections declare.</param>
    /// <returns>The message a startup failure carries.</returns>
    public static string DescribeReusedAlias(string reusedAlias) =>
        $"A chat model and an embedding endpoint both declare the alias '{reusedAlias}'. An alias names one "
        + "endpoint, because it is what a credential, a resilience circuit, and a log line are keyed by.";

    /// <summary>Finds the decision model alias a declared chat model reuses, if any.</summary>
    /// <param name="decisions">The bound decision declaration, or <see langword="null" /> when the deployment wrote no section.</param>
    /// <param name="chat">The bound chat declaration, or <see langword="null" /> when the deployment wrote no section.</param>
    /// <returns>The reused alias as the decision section spells it, or <see langword="null" /> when nothing collides.</returns>
    /// <remarks>
    /// Judged from the chat side because the chat section is the one that reloads: a decision model is fixed for the
    /// life of the process, so a collision can only arrive with a chat candidate — and the credential source finds a
    /// chat model before a decision model, so a chat key would otherwise be presented at the decision model's address.
    /// </remarks>
    public static string? FindReusedDecisionAlias(DecisionModelOptions? decisions, ChatModelOptions? chat)
    {
        if (chat is null || decisions is null)
        {
            return null;
        }

        var chatAliases = chat.DeclaredAliases;

        return decisions.DeclaredAliases.FirstOrDefault(alias => chatAliases.Contains(alias, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Describes a chat model reusing a decision model's alias, naming the alias and what it costs to leave it.</summary>
    /// <param name="reusedAlias">The alias both sections declare.</param>
    /// <returns>The message a startup failure or a refused reload carries.</returns>
    public static string DescribeReusedDecisionAlias(string reusedAlias) =>
        $"A chat model and a decision model both declare the alias '{reusedAlias}'. An alias names one "
        + "endpoint, because it is what a credential, a resilience circuit, and a log line are keyed by.";
}
