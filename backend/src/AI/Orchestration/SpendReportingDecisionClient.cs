// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.DecisionModels;
using MailFathom.AI.Decisions;
using MailFathom.Application.Chat;
using MailFathom.Application.Retrieval.AskMail;

namespace MailFathom.AI.Orchestration;

/// <summary>Charges what each decision call consumed to the deployment's period, in tokens and never in money.</summary>
/// <remarks>
/// <para>
/// What a run reports under
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0022-what-an-ai-run-reports-about-cost-cancellation-and-the-model.md">ADR 0022</see>
/// is its own consumption: the calls it made and the tokens the provider counted. This charges exactly that to the same
/// period ledger every question and every derivation is charged to, so a decision is visible in the answering spend a
/// deployment already publishes. A provider that states a price beside its counts is not believed and not converted:
/// MailFathom holds no price list and reports no money, so whatever such a figure was, it stays in the response's raw
/// representation and goes no further.
/// </para>
/// <para>
/// Charged as each call returns rather than once when the run ends, because a decision run is one call, and charging it
/// at release would make a synchronous disposal — which a container may perform — the place a spend is lost.
/// </para>
/// </remarks>
internal sealed class SpendReportingDecisionClient : DelegatingDecisionClient
{
    private readonly IMailAnsweringSpendLedger spendLedger;

    /// <summary>Initializes the middleware over the client whose calls it charges.</summary>
    /// <param name="innerClient">The client every call is passed to.</param>
    /// <param name="spendLedger">The deployment's period ledger.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal SpendReportingDecisionClient(IDecisionClient innerClient, IMailAnsweringSpendLedger spendLedger)
        : base(innerClient)
    {
        ArgumentNullException.ThrowIfNull(spendLedger);

        this.spendLedger = spendLedger;
    }

    /// <inheritdoc />
    public override async Task<DecisionResponse> DecideAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> input,
        IReadOnlyList<DecisionQuestion> questions,
        DecisionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        DecisionResponse response;

        try
        {
            response = await base.DecideAsync(input, questions, options, cancellationToken);
        }
        catch (DecisionFailedException failure) when (failure.Usage is not null)
        {
            // An answer refused as unreadable was still run and billed, so it is charged exactly as a readable one is.
            await this.ChargeAsync(failure.Usage);

            throw;
        }

        await this.ChargeAsync(response.Usage);

        return response;
    }

    private async Task ChargeAsync(Microsoft.Extensions.AI.UsageDetails? reported)
    {
        if (reported is null || (reported.InputTokenCount ?? 0) + (reported.OutputTokenCount ?? 0) <= 0)
        {
            return;
        }

        // Uncancellable, because the tokens were already spent: a caller that stops waiting now does not make the
        // provider's count untrue, and a period that missed it would admit more than its ceiling allows.
        await this.spendLedger.RecordSpendAsync(
            new ChatTokenUsage(reported.InputTokenCount ?? 0, reported.OutputTokenCount ?? 0),
            CancellationToken.None);
    }
}
