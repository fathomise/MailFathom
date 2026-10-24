// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Localization;

/// <summary>A sentence the service itself writes for a person to read, named once so no caller can misspell its key.</summary>
/// <remarks>The member's name is the key its sentence is stored under in every language's resource file.</remarks>
public enum ApplicationText
{
    /// <summary>The agent's note written into a conversation when the person stops the answer being composed.</summary>
    AgentRunStoppedNote = 0,

    /// <summary>The status line an Agent answer shows before it has looked anything up.</summary>
    AgentStatusReadingQuestion = 1,

    /// <summary>The status line while the Agent searches the person's mail.</summary>
    AgentStatusSearchingMail = 2,

    /// <summary>The status line while the Agent reads one thread of mail.</summary>
    AgentStatusReadingThread = 3,

    /// <summary>The status line while the Agent reads the person's calendar.</summary>
    AgentStatusReadingCalendar = 4,

    /// <summary>The status line while the Agent reads the person's tasks.</summary>
    AgentStatusReadingTasks = 5,

    /// <summary>The status line while the Agent composes something the person will be asked to approve.</summary>
    AgentStatusPreparingProposal = 6,

    /// <summary>The status line while the Agent writes the answer itself.</summary>
    AgentStatusComposingAnswer = 7,

    /// <summary>What a Discover result says in place of an answer when the mail the run read does not answer the question.</summary>
    DiscoveryMailDoesNotAnswer = 8,

    /// <summary>What an outbox entry says when the mail server refused the message for good.</summary>
    OutboxFailureRefused = 9,

    /// <summary>What an outbox entry says when every attempt to send the message failed.</summary>
    OutboxFailureAttemptsExhausted = 10,

    /// <summary>What an outbox entry says when the message went out and the server's answer never came back.</summary>
    OutboxFailureOutcomeUnknown = 11,

    /// <summary>What an outbox entry says when the time the message was to leave at passed too long ago to send it unasked.</summary>
    OutboxFailureDueTimeMissed = 12,

    /// <summary>What an outbox entry says when the account configures no address to send from.</summary>
    OutboxFailureSenderUnconfigured = 13,

    /// <summary>What an outbox entry says when the message is larger than the mail server or the deployment accepts.</summary>
    OutboxFailureBoundExceeded = 14,

    /// <summary>What an outbox entry says when the mail server could not be reached.</summary>
    OutboxFailureServerUnreachable = 15,

    /// <summary>What an outbox entry says about a failure it has no sentence of its own for.</summary>
    OutboxFailureOther = 16,
}
