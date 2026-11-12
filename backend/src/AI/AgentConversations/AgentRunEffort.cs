// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel;
using MailFathom.Application.Agent.Answering;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Retrieval.AskMail;
using Microsoft.Extensions.AI;

namespace MailFathom.AI.AgentConversations;

/// <summary>How far one Agent run may go: the mode it was asked in, the ledger its ceilings are kept in, and the one move an automatic run may make to the deep ones.</summary>
/// <remarks>
/// <para>
/// <strong>The move is the model's to ask for.</strong> An automatic run starts under the fast ceilings and is offered
/// one tool more than a fast or a deep one: asking for the deep ceilings, with a sentence saying why. Nothing here
/// judges the question; the model reads it and the mail, and the tool's own description says when to ask. The move is
/// written into the conversation the moment it is made, and the ledger refuses a second one.
/// </para>
/// <para>
/// <strong>A run that reaches its ceiling answers.</strong> Its last call is made without the reading tools — see
/// <see cref="LastCallChatClient" /> — and the answer it writes then is recorded as one that stopped at a ceiling, as is
/// one whose reading the retrieved-character ceiling cut. A fast run never moves on its own; its record says it stopped,
/// in a form a client offers the deep mode from.
/// </para>
/// </remarks>
internal sealed class AgentRunEffort
{
    /// <summary>Names the tool an automatic run asks for the deep ceilings with.</summary>
    internal const string GoDeepToolName = "go_deep";

    /// <summary>The longest reason for a move that is recorded, which keeps it the one sentence the client shows.</summary>
    internal const int MaximumReasonLength = 300;

    private const string GoDeepDescription =
        "Moves this answer from a quick reading to a thorough one, which may read several times more mail and take "
        + "minutes rather than seconds. Call it once, before you read further, when the person asks for a complete or "
        + "thorough answer — every message of a long correspondence, all of a period, everything one sender sent — or "
        + "when a reading says this run's limit cut it off and the question needs the mail it left out. Do not call it "
        + "for a question a few messages answer.";

    private readonly AgentRunBounds bounds;
    private readonly AgentAnswerJournal journal;
    private int answeredAtLastCall;

    /// <summary>Initializes the effort of one run.</summary>
    /// <param name="mode">The mode the question was asked in.</param>
    /// <param name="bounds">The fast and the deep ceilings.</param>
    /// <param name="journal">Where the move is written the moment it is made.</param>
    internal AgentRunEffort(AgentAnswerMode mode, AgentRunBounds bounds, AgentAnswerJournal journal)
    {
        this.Mode = mode;
        this.bounds = bounds;
        this.journal = journal;
        this.Ledger = new MailAnsweringRunLedger(bounds.StartingFor(mode));
    }

    /// <summary>Gets the mode the question was asked in.</summary>
    internal AgentAnswerMode Mode { get; }

    /// <summary>Gets the ledger the run's ceilings are kept in, which every call and every reading spends from.</summary>
    internal MailAnsweringRunLedger Ledger { get; }

    /// <summary>Gets whether the run may still ask for the deep ceilings, which only an automatic run that has not moved may.</summary>
    internal bool MayDeepen => this.Mode is AgentAnswerMode.Auto && !this.HasDeepened;

    /// <summary>Gets whether an automatic run has moved to the deep ceilings.</summary>
    internal bool HasDeepened { get; private set; }

    /// <summary>Gets the ceiling the run answered at, or <see langword="null" /> where it answered within its mode.</summary>
    internal AgentRunCeiling? Ceiling =>
        Volatile.Read(ref this.answeredAtLastCall) is 1 || this.Ledger.RetrievalWasTruncated
            ? new AgentRunCeiling(this.Ledger.Read().MessagesRetrieved)
            : null;

    /// <summary>Adds the move to the tools a run is offered, where the run may make it.</summary>
    /// <param name="tools">The tools the person's grant allows.</param>
    /// <returns>Those tools, and the move after them for an automatic run.</returns>
    internal IReadOnlyList<AITool> Offer(IReadOnlyList<AITool> tools) =>
        this.MayDeepen ? [.. tools, AIFunctionFactory.Create(this.GoDeepAsync, GoDeepToolName, GoDeepDescription)] : tools;

    /// <summary>Notes that the run is about to make the last call its ceilings leave room for.</summary>
    internal void NoteLastCall() => Volatile.Write(ref this.answeredAtLastCall, 1);

    [Description("Moves this answer to a thorough reading.")]
    private async Task<string> GoDeepAsync(
        [Description("One sentence in the person's language saying why this question needs a thorough reading, naming what it has to read — such as how far back it reaches or how many messages it covers. The person is shown it.")] string reason,
        CancellationToken cancellationToken)
    {
        if (!this.MayDeepen)
        {
            return "This answer already reads thoroughly. Carry on reading what the question needs, then answer.";
        }

        var stated = new string([.. (reason ?? string.Empty).ReplaceLineEndings(" ").Where(static character => !char.IsControl(character))]).Trim();

        if (stated.Length is 0 or > MaximumReasonLength || !PresentationText.TryCreate(stated, out var said))
        {
            return $"Say in one sentence of at most {MaximumReasonLength} characters why the question needs a thorough reading.";
        }

        if (!this.Ledger.Widen(this.bounds.Deep))
        {
            return "This answer already reads thoroughly. Carry on reading what the question needs, then answer.";
        }

        this.HasDeepened = true;
        Volatile.Write(ref this.answeredAtLastCall, 0);

        if (!await this.journal.RecordDeepeningAsync(said, cancellationToken))
        {
            this.journal.Stopping.ThrowIfCancellationRequested();
        }

        return "This answer now reads thoroughly: read what the question needs — repeat a lookup a limit cut short — then answer.";
    }
}
