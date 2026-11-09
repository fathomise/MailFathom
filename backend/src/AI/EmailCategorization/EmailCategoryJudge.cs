// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.DecisionModels;
using MailFathom.AI.Decisions;
using MailFathom.Application.Categories;
using MailFathom.Application.Decisions;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.Spam.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.EmailCategorization;

/// <summary>Asks the decision model routed to categorization about one message, in one request, through the composed decision client.</summary>
/// <remarks>
/// Built as the spam model stage's judge is: one message is one run, admitted against the same period ledger and charged
/// by the same client composition, and every failure is an outcome. A failed call does not fall back to another model,
/// because a probability is calibrated against one build. Nothing the provider sent and nothing of the message reaches a
/// log line.
/// </remarks>
internal sealed class EmailCategoryJudge : IEmailCategoryJudge, IDisposable
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly int maximumRequestCharacters;
    private readonly SemaphoreSlim callSlots;
    private readonly ILogger<EmailCategoryJudge> logger;

    /// <summary>Initializes the judge.</summary>
    /// <param name="scopeFactory">Opens the scope one judgement's client, ceiling, and period charge belong to.</param>
    /// <param name="egressGuard">Withholds from the provider whatever the account's posture withholds.</param>
    /// <param name="bounds">How many calls one replica runs at once.</param>
    /// <param name="maximumRequestCharacters">The most characters a request body to the declared model may hold.</param>
    /// <param name="logger">Records how a judgement ended, never what it was about.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maximumRequestCharacters" /> is not positive.</exception>
    public EmailCategoryJudge(
        IServiceScopeFactory scopeFactory,
        SensitiveContentEgressGuard egressGuard,
        SpamModelStageBounds bounds,
        int maximumRequestCharacters,
        ILogger<EmailCategoryJudge> logger)
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
    /// <remarks>This one is registered only where a decision model serves categorization, so it is active by construction.</remarks>
    public bool IsActive => true;

    /// <inheritdoc />
    public async Task<EmailCategoryJudgement> JudgeAsync(
        EmailCategoryEvidence evidence,
        IReadOnlyList<OwnMailCategory> ownCategories,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(ownCategories);

        await using var scope = this.scopeFactory.CreateAsyncScope();

        if (!await scope.ServiceProvider.GetRequiredService<IMailAnsweringSpendLedger>().TryAdmitRunAsync(cancellationToken))
        {
            return this.Unanswered(DecisionModelOutcome.BudgetExhausted, modelAlias: null);
        }

        DecisionQuestion[] ownQuestions = [.. ownCategories.Select(EmailCategoryQuestions.For)];
        DecisionQuestion[] questions = [EmailCategoryQuestions.Default, .. ownQuestions];
        string input;

        try
        {
            input = await EmailCategoryTurn.ComposeAsync(
                evidence,
                this.egressGuard,
                Math.Max(0, this.maximumRequestCharacters - DecisionQuestionSet.ReservedCharactersOf(questions)),
                cancellationToken);
        }
        catch (SensitiveContentScannerUnavailableException)
        {
            return this.Unanswered(DecisionModelOutcome.ScannerUnavailable, modelAlias: null);
        }

        var client = scope.ServiceProvider.GetRequiredKeyedService<IDecisionClient>(DecisionFunction.EmailCategorization);
        var requestedModel = client.GetService<DecisionClientMetadata>()?.DefaultModelId ?? string.Empty;

        var (response, outcome, alias) = await DecisionConsultation.AskAsync(client, input, questions, this.callSlots, cancellationToken);

        if (response is null)
        {
            return this.Unanswered(outcome, alias);
        }

        if (EmailCategoryReading.Read(response, ownCategories, ownQuestions, requestedModel) is not { } assignments)
        {
            return this.Unanswered(DecisionModelOutcome.AnswerUnreadable, alias);
        }

        EmailCategorizationEvents.LogAnswered(this.logger);

        return EmailCategoryJudgement.Answered(assignments);
    }

    /// <inheritdoc />
    public void Dispose() => this.callSlots.Dispose();

    private EmailCategoryJudgement Unanswered(DecisionModelOutcome outcome, string? modelAlias)
    {
        EmailCategorizationEvents.LogUnanswered(this.logger, outcome, modelAlias ?? "(not reached)");

        return EmailCategoryJudgement.Unanswered(outcome);
    }
}
