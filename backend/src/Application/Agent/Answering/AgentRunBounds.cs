// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Retrieval.AskMail;

namespace MailFathom.Application.Agent.Answering;

/// <summary>The two sets of ceilings an Agent run may be held to: the fast ones every answering run has, and the deep ones a question may ask for.</summary>
/// <remarks>
/// <para>
/// Two sets rather than a multiplier, because the three ceilings in each fail in three different ways and an operator
/// widens them for different reasons: the retrieved characters are how much of a mailbox may leave the process for one
/// question, the calls how long the tool loop may go round, and the tokens what it costs.
/// </para>
/// <para>
/// Every deep ceiling is at least its fast one. A deep ceiling below would make the mode that reads further read less,
/// and an <see cref="AgentAnswerMode.Auto" /> run that moved would find its ceilings lowered under it.
/// </para>
/// </remarks>
public sealed record AgentRunBounds
{
    /// <summary>Initializes the bounds.</summary>
    /// <param name="fast">The ceilings a fast run is held to, and an automatic one until it moves.</param>
    /// <param name="deep">The ceilings a deep run is held to, and an automatic one once it moves.</param>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when any deep ceiling is below its fast one.</exception>
    public AgentRunBounds(MailAnsweringRunBounds fast, MailAnsweringRunBounds deep)
    {
        ArgumentNullException.ThrowIfNull(fast);
        ArgumentNullException.ThrowIfNull(deep);

        if (deep.MaximumRetrievedCharacters < fast.MaximumRetrievedCharacters
            || deep.MaximumProviderCalls < fast.MaximumProviderCalls
            || deep.MaximumTokens < fast.MaximumTokens)
        {
            throw new ArgumentException("Every deep ceiling is at least the fast one beside it.", nameof(deep));
        }

        this.Fast = fast;
        this.Deep = deep;
    }

    /// <summary>Gets the ceilings a deployment that states none holds a deep run to.</summary>
    /// <remarks>
    /// Five times the retrieved mail, three times the calls, and a little over six times the tokens of a fast run: room
    /// to read a long correspondence or a year of one sender's mail, with every call carrying what the calls before it
    /// read. One such run may take half of a default period's tokens, which is the number an operator weighs it by.
    /// </remarks>
    public static MailAnsweringRunBounds DefaultDeep { get; } = MailAnsweringRunBounds.Create(100_000, 24, 1_000_000);

    /// <summary>Gets the bounds a deployment that states none receives.</summary>
    public static AgentRunBounds Default { get; } = new(MailAnsweringRunBounds.Default, DefaultDeep);

    /// <summary>Gets the ceilings a fast run is held to, and an automatic one until it moves.</summary>
    public MailAnsweringRunBounds Fast { get; }

    /// <summary>Gets the ceilings a deep run is held to, and an automatic one once it moves.</summary>
    public MailAnsweringRunBounds Deep { get; }

    /// <summary>Gets the ceilings a run asked in one mode starts under.</summary>
    /// <param name="mode">The mode the question was asked in.</param>
    /// <returns>The deep ceilings for a deep question, and the fast ones for every other.</returns>
    public MailAnsweringRunBounds StartingFor(AgentAnswerMode mode) => mode is AgentAnswerMode.Deep ? this.Deep : this.Fast;
}
