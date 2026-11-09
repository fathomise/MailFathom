// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Retrieval.AskMail;
using Microsoft.Extensions.AI;

namespace MailFathom.Evaluations.Answering;

/// <summary>Reads a run's ledger as each provider call starts, which is the moment a deployment checks its bounds.</summary>
/// <remarks>
/// Composed beneath the budgeting client, so a reading counts the call it was taken for among the calls and none of that
/// call's tokens among the tokens — exactly what <see cref="MailAnsweringRunLedger.RequireAllowanceForNextCall" /> had
/// just weighed. Nothing here streams, because the budgeting client above refuses to.
/// </remarks>
/// <param name="model">The model the calls reach.</param>
/// <param name="runLedger">The ledger the budgeting client above charges.</param>
internal sealed class CallStartRecordingChatClient(IChatClient model, MailAnsweringRunLedger runLedger) : DelegatingChatClient(model)
{
    /// <summary>Gets the ledger as the run's last call started, or <see langword="null" /> where the run made no call.</summary>
    public MailAnsweringRunSpend? AtLastCallStart { get; private set; }

    /// <inheritdoc />
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        this.AtLastCallStart = runLedger.Read();

        return base.GetResponseAsync(messages, options, cancellationToken);
    }
}
