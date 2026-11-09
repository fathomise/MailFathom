// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.DecisionModels;
using MailFathom.AI.Decisions;
using MailFathom.Application.Decisions;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.Spam.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.SpamClassification;

/// <summary>Asks the decision model routed to spam classification about one message, through the composed decision client.</summary>
/// <remarks>
/// <para>
/// Nothing here reaches a provider directly. The call goes through the client <see cref="AiServiceCollectionExtensions" />
/// composes for <see cref="DecisionFunction.SpamClassification" />, inside the run ceiling, the charge to the period,
/// the telemetry, and the resilience budget it carries, so each call is reported as ADR 0022 requires.
/// </para>
/// <para>
/// One message is one run. Each judgement opens a scope of its own and resolves its client there, so the run ceiling the
/// outermost middleware holds is this message's alone, whatever scope the classification itself runs in — a run over a
/// mailbox classifies a batch in one scope, and sharing one ceiling across it would refuse the batch's later messages
/// for its earlier ones. The judgement is admitted against the period before anything is guarded or composed, so a
/// period already spent costs nothing to refuse.
/// </para>
/// <para>
/// How many calls run at once is bounded per replica by the deployment, because a classification backlog — the first
/// run over a mailbox, or an arrival burst — would otherwise open as many calls as the queue has workers.
/// </para>
/// <para>
/// Every failure is an outcome. Nothing the provider sent, and nothing of the message, reaches a log line: the outcome
/// and the model's alias are the whole of what is recorded.
/// </para>
/// </remarks>
internal sealed class SpamModelJudge : ISpamModelJudge, IDisposable
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly int maximumRequestCharacters;
    private readonly SemaphoreSlim callSlots;
    private readonly ILogger<SpamModelJudge> logger;

    /// <summary>Initializes the judge.</summary>
    /// <param name="scopeFactory">Opens the scope one judgement's client, ceiling, and period charge belong to.</param>
    /// <param name="egressGuard">Withholds from the provider whatever the account's posture withholds.</param>
    /// <param name="bounds">How many calls one replica runs at once.</param>
    /// <param name="maximumRequestCharacters">The most characters a request body to the declared model may hold, which the input is cut to fit.</param>
    /// <param name="logger">Records how a judgement ended, never what it was about.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maximumRequestCharacters" /> is not positive.</exception>
    public SpamModelJudge(
        IServiceScopeFactory scopeFactory,
        SensitiveContentEgressGuard egressGuard,
        SpamModelStageBounds bounds,
        int maximumRequestCharacters,
        ILogger<SpamModelJudge> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(egressGuard);
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRequestCharacters);
        ArgumentNullException.ThrowIfNull(logger);

        this.scopeFactory = scopeFactory;
        this.egressGuard = egressGuard;
        this.maximumRequestCharacters = maximumRequestCharacters;
        this.callSlots = new SemaphoreSlim(bounds.MaximumConcurrentCalls, bounds.MaximumConcurrentCalls);
        this.logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>This one is registered only where a decision model serves spam classification, so it is active by construction.</remarks>
    public bool IsActive => true;

    /// <inheritdoc />
    public async Task<SpamModelJudgement> JudgeAsync(
        SpamModelEvidence evidence,
        SpamModelJob job,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        await using var scope = this.scopeFactory.CreateAsyncScope();

        if (!await scope.ServiceProvider.GetRequiredService<IMailAnsweringSpendLedger>().TryAdmitRunAsync(cancellationToken))
        {
            return this.Unanswered(job, DecisionModelOutcome.BudgetExhausted, modelAlias: null);
        }

        string input;

        try
        {
            input = await SpamClassificationTurn.ComposeAsync(
                evidence,
                this.egressGuard,
                this.maximumRequestCharacters,
                job,
                cancellationToken);
        }
        catch (SensitiveContentScannerUnavailableException)
        {
            return this.Unanswered(job, DecisionModelOutcome.ScannerUnavailable, modelAlias: null);
        }

        var client = scope.ServiceProvider.GetRequiredKeyedService<IDecisionClient>(DecisionFunction.SpamClassification);
        var requestedModel = client.GetService<DecisionClientMetadata>()?.DefaultModelId ?? string.Empty;

        var (response, outcome, alias) = await DecisionConsultation.AskAsync(
            client,
            input,
            SpamClassificationQuestions.For(job),
            this.callSlots,
            cancellationToken);

        if (response is null)
        {
            return this.Unanswered(job, outcome, alias);
        }

        if (SpamClassificationReading.Read(response, job, requestedModel) is not { } answer)
        {
            return this.Unanswered(job, DecisionModelOutcome.AnswerUnreadable, alias);
        }

        SpamClassificationEvents.LogAnswered(this.logger, job);

        return SpamModelJudgement.Answered(answer);
    }

    /// <inheritdoc />
    public void Dispose() => this.callSlots.Dispose();

    private SpamModelJudgement Unanswered(SpamModelJob job, DecisionModelOutcome outcome, string? modelAlias)
    {
        SpamClassificationEvents.LogUnanswered(this.logger, job, outcome, modelAlias ?? "(not reached)");

        return SpamModelJudgement.Unanswered(outcome);
    }
}
