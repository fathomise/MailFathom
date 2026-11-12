// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Agent.Conversations;

/// <summary>A run reached a ceiling of the mode it ran in and answered from what it had read by then.</summary>
/// <remarks>
/// Carried on the ending of an answer that completed rather than on one that failed: the run still answered, so what
/// the person is told is that the answer may be partial, and a client offers the same question again in a deeper mode
/// from here. Nothing in it names a ceiling, a count of tokens, or a model, for the reason a refusal for budget names
/// none — the operator reads those from the meter.
/// </remarks>
public sealed record AgentRunCeiling
{
    /// <summary>Initializes the record.</summary>
    /// <param name="messagesRead">How many messages the run had read when it stopped.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="messagesRead" /> is negative.</exception>
    public AgentRunCeiling(int messagesRead)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(messagesRead);

        this.MessagesRead = messagesRead;
    }

    /// <summary>Gets how many messages the run had read when it stopped.</summary>
    public int MessagesRead { get; }
}
