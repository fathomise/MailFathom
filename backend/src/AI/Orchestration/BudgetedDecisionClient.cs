// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.Decisions;
using MailFathom.Application.Chat;
using MailFathom.Application.Retrieval.AskMail;

namespace MailFathom.AI.Orchestration;

/// <summary>Holds each decision call to the ceilings of the run that makes it.</summary>
/// <remarks>
/// <para>
/// The decision counterpart of the run half of <see cref="BudgetedChatClient" />, over the same
/// <see cref="MailAnsweringRunLedger" />: a call the run's allowance no longer covers is refused before anything is
/// sent, and the tokens a call consumed are recorded against the run before its answer is handed back.
/// </para>
/// <para>
/// Composed outside the resilience middleware, so a call this deployment's own ceiling refused never reaches the
/// model's circuit, its concurrency budget, or its health record. What the call is charged to the deployment's period is
/// <see cref="SpendReportingDecisionClient" />'s.
/// </para>
/// </remarks>
internal sealed class BudgetedDecisionClient : DelegatingDecisionClient
{
    private readonly MailAnsweringRunLedger runLedger;

    /// <summary>Initializes the middleware over the client a run asks through.</summary>
    /// <param name="innerClient">The client every admitted call is passed to.</param>
    /// <param name="runLedger">The run's own ceilings and what it has spent against them.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal BudgetedDecisionClient(IDecisionClient innerClient, MailAnsweringRunLedger runLedger)
        : base(innerClient)
    {
        ArgumentNullException.ThrowIfNull(runLedger);

        this.runLedger = runLedger;
    }

    /// <inheritdoc />
    /// <exception cref="MailAnsweringBudgetExhaustedException">Thrown when the run has no allowance left for another call.</exception>
    public override async Task<DecisionResponse> DecideAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> input,
        IReadOnlyList<DecisionQuestion> questions,
        DecisionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        this.runLedger.RequireAllowanceForNextCall();

        var response = await base.DecideAsync(input, questions, options, cancellationToken);

        if (response.Usage is { } reported)
        {
            this.runLedger.RecordSpend(new ChatTokenUsage(reported.InputTokenCount ?? 0, reported.OutputTokenCount ?? 0));
        }

        return response;
    }
}
