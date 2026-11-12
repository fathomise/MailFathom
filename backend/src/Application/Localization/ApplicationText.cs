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

    /// <summary>The words a read receipt the reader chose to send carries for the person who asked for it.</summary>
    ReadReceiptDisplayed = 17,

    /// <summary>The status line while the Agent lists the person's mail in the order it arrived.</summary>
    AgentStatusListingMail = 18,

    /// <summary>The status line while the Agent counts the person's mail.</summary>
    AgentStatusCountingMail = 19,

    /// <summary>The status line while the Agent searches or reads the files the person handed to the conversation.</summary>
    AgentStatusReadingFiles = 20,

    /// <summary>The word a report the Agent writes is labelled with above its title.</summary>
    AgentReportKind = 21,

    /// <summary>When a report was written, under its title; takes the date and time already formatted.</summary>
    AgentReportWritten = 22,

    /// <summary>Which page of how many, in a report's running foot; takes the page and the count.</summary>
    AgentReportPage = 23,

    /// <summary>The heading of the list of sources a report closes with.</summary>
    AgentReportSources = 24,

    /// <summary>What the list of a report's sources holds, under its heading.</summary>
    AgentReportSourcesIntroduction = 25,

    /// <summary>What a report's list of sources says where the report cites no message and no file.</summary>
    AgentReportNoSources = 26,

    /// <summary>The heading over the files attached to the question a report cites.</summary>
    AgentReportAttachedFiles = 27,

    /// <summary>Who a file a report cites came from, which is always the person who asked.</summary>
    AgentReportAttachedByYou = 28,

    /// <summary>What a report rests on, said under its title, around the count of messages, of files, or of both.</summary>
    AgentReportBasis = 29,

    /// <summary>The count of messages a report rests on beside the count of files, inside <see cref="AgentReportBasis" />.</summary>
    AgentReportBasisMessagesAndFiles = 30,

    /// <summary>One message a report rests on, inside <see cref="AgentReportBasis" />.</summary>
    AgentReportBasisOneMessage = 31,

    /// <summary>Several messages a report rests on, inside <see cref="AgentReportBasis" />.</summary>
    AgentReportBasisMessages = 32,

    /// <summary>One attached file a report rests on, inside <see cref="AgentReportBasis" />.</summary>
    AgentReportBasisOneFile = 33,

    /// <summary>Several attached files a report rests on, inside <see cref="AgentReportBasis" />.</summary>
    AgentReportBasisFiles = 34,

    /// <summary>The sentence opening a report's sources where it cites the question's files as well as messages.</summary>
    AgentReportSourcesIntroductionWithFiles = 35,
}
