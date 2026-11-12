// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Retrieval.AskMail;
using Microsoft.Extensions.AI;

namespace MailFathom.AI.AgentConversations;

/// <summary>Turns the last call a run's ceilings leave room for into the call that answers.</summary>
/// <remarks>
/// <para>
/// A run whose model keeps reading reaches its ceiling with nothing written, and the ledger then refuses the call that
/// would have answered — so the person gets nothing for a question that spent the whole run. This client sees that
/// call coming, through <see cref="MailAnsweringRunLedger.HasRoomForAtMost" />, and makes it one the model can only
/// answer on: no tool may be called, and the model is told to answer from what it read and to say it stopped there.
/// </para>
/// <para>
/// An automatic run that has not moved is offered the move alone one call earlier, so a question that turned out to
/// need more reading than a fast answer allows goes deep rather than stopping. That call is the second to last rather
/// than the last, so a model that answers it with some other tool still has the last one to answer on, and an answer
/// written on it is one the model chose to write rather than one a ceiling forced, so it is not recorded as stopped. A fast run is
/// never offered the move: it never moves on its own, and a deep run has nowhere further to go.
/// </para>
/// <para>
/// The same run is told to consider the move as soon as a reading was cut at the fast limit, on every call until it
/// moves or answers. The reading's own mark and the move's description say as much, and a model still answers with a
/// caveat more often than it asks; said at the turn it is about to take, with every tool still offered, it asks.
/// </para>
/// <para>
/// It sits beneath the framework's tool loop, like the steering client, so <em>the next call</em> is the next call to
/// the model; nothing it adds is recorded, because it is this deployment's instruction rather than anything the person
/// or the model said.
/// </para>
/// </remarks>
internal sealed class LastCallChatClient : DelegatingChatClient
{
    private const string AnswerNow =
        "This is the last reply this answer may make: no tool can be called any more. Answer the question now from what "
        + "you have already read, and say plainly in your answer that you stopped at the limit of what this answer may "
        + "read, so mail you did not read may change it.";

    private const string AnswerOrGoDeep =
        "A quick answer may read no further after this reply. Either answer the question now from what you have already "
        + "read, or — where the question needs more reading than you have done — call " + AgentRunEffort.GoDeepToolName
        + " alone and carry on reading.";

    private const string CutShortGoDeep =
        "A reading above was cut at the limit of a quick answer. Where the question needs the mail that reading left "
        + "out — a total, a largest or latest, every message of a kind — call " + AgentRunEffort.GoDeepToolName
        + " now and repeat the reading, rather than answering with a caveat. Answer from what you have only where it "
        + "already settles the question.";

    private readonly AgentRunEffort effort;

    /// <summary>Initializes the client over the one the run sends through.</summary>
    /// <param name="innerClient">The client every call is sent through.</param>
    /// <param name="effort">The run's mode and ledger.</param>
    internal LastCallChatClient(IChatClient innerClient, AgentRunEffort effort)
        : base(innerClient) => this.effort = effort;

    /// <inheritdoc />
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var ledger = this.effort.Ledger;

        if (ledger.HasRoomForAtMost(1))
        {
            this.effort.NoteLastCall();

            var last = options?.Clone() ?? new ChatOptions();
            last.ToolMode = ChatToolMode.None;

            return base.GetResponseAsync([.. messages, new ChatMessage(ChatRole.User, AnswerNow)], last, cancellationToken);
        }

        if (this.effort.MayDeepen && ledger.HasRoomForAtMost(2))
        {
            var lastFast = options?.Clone() ?? new ChatOptions();
            lastFast.Tools = [.. (lastFast.Tools ?? []).Where(static tool => tool.Name == AgentRunEffort.GoDeepToolName)];

            return base.GetResponseAsync([.. messages, new ChatMessage(ChatRole.User, AnswerOrGoDeep)], lastFast, cancellationToken);
        }

        return this.effort.MayDeepen && ledger.RetrievalWasTruncated
            ? base.GetResponseAsync([.. messages, new ChatMessage(ChatRole.User, CutShortGoDeep)], options, cancellationToken)
            : base.GetResponseAsync(messages, options, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>Releases nothing, for the reason <see cref="SteeredChatClient" /> releases nothing: the client beneath it is released by the run's owner.</remarks>
    protected override void Dispose(bool disposing) => base.Dispose(disposing: false);
}
