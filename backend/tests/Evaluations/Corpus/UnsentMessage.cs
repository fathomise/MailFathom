// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Provenance;

namespace MailFathom.Evaluations.Corpus;

/// <summary>A message the mailbox has not sent, and the check that an answer about it says which state it is in rather than reading it as delivered.</summary>
/// <remarks>
/// Whether an answer reads the message as delivered is prose, and the judge grades it against the instruction. What a
/// structure settles is that the answer names the state at all, because the instruction asks for exactly that: an answer
/// that never says the reply is scheduled, a draft, still being sent, or failed has told the person it was received.
/// Each state is recognized by the words an English answer names it with, and the list is widened when an answer names
/// it in words the list lacks, never the check.
/// </remarks>
/// <param name="Message">The message, whose recorded provenance carries the state.</param>
internal sealed record UnsentMessage(CorpusMessage Message)
{
    /// <summary>The name the check is recorded under.</summary>
    public const string MetricName = "Says the mail has not been sent";

    /// <summary>Gets the state the outbox holds the message in.</summary>
    public UnsentEmailState State =>
        this.Message.Provenance.Unsent ?? throw new InvalidOperationException($"Message {this.Message.Id} records no unsent state.");

    /// <summary>Gets the words an answer naming the state uses, any one of which is enough.</summary>
    public IReadOnlyList<string> StateWords => this.State switch
    {
        UnsentEmailState.Draft => ["draft"],
        UnsentEmailState.Scheduled => ["scheduled", "schedule"],
        UnsentEmailState.Sending => ["being sent", "sending", "outbox", "not been transmitted", "not yet been transmitted", "not yet transmitted", "waiting to be sent", "waiting to go out"],
        UnsentEmailState.Failed => ["failed", "could not be sent", "couldn't be sent", "did not go out", "didn't go out", "was not delivered", "wasn't delivered", "never left", "refused", "rejected"],
        _ => throw new ArgumentOutOfRangeException(nameof(this.State), this.State, "The state is one no case writes."),
    };

    /// <summary>Names how an answer failed to say the message was not sent, or answers <see langword="null" /> where it said which state it is in.</summary>
    /// <param name="answer">What the operation wrote for the person to read.</param>
    /// <returns>What the answer left out, in words a failed run can be read by.</returns>
    public string? Shortfall(string? answer) =>
        this.StateWords.Any(word => (answer ?? string.Empty).Contains(word, StringComparison.OrdinalIgnoreCase))
            ? null
            : $"The answer never says the message is {Named(this.State)}, so it reads as delivered.";

    /// <summary>Describes the outcome the check recorded, for a verdict that passed it.</summary>
    /// <returns>The sentence the metric carries when the state was named.</returns>
    public string Reported() => $"The answer says the message is {Named(this.State)}.";

    private static string Named(UnsentEmailState state) => state switch
    {
        UnsentEmailState.Draft => "a draft",
        UnsentEmailState.Scheduled => "scheduled",
        UnsentEmailState.Sending => "still being sent",
        UnsentEmailState.Failed => "a failed send",
        _ => state.ToString(),
    };
}
