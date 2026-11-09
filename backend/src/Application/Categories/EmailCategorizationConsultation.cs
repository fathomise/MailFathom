// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Decisions;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Spam.Model;

namespace MailFathom.Application.Categories;

/// <summary>Reads one stored message and asks a decision model about it, for the arrival stage and a run over stored mail alike.</summary>
public sealed class EmailCategorizationConsultation
{
    private readonly IEmailCategoryJudge judge;
    private readonly IEmailContentStore contentStore;
    private readonly SpamModelEvidenceReader evidenceReader;
    private readonly IEmailCategorizationTelemetry telemetry;

    /// <summary>Initializes the consultation.</summary>
    /// <param name="judge">Asks the model.</param>
    /// <param name="contentStore">Reads the stored message.</param>
    /// <param name="evidenceReader">Reads what the model is shown.</param>
    /// <param name="telemetry">Counts how each consultation ended.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public EmailCategorizationConsultation(
        IEmailCategoryJudge judge,
        IEmailContentStore contentStore,
        SpamModelEvidenceReader evidenceReader,
        IEmailCategorizationTelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(judge);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(evidenceReader);
        ArgumentNullException.ThrowIfNull(telemetry);

        this.judge = judge;
        this.contentStore = contentStore;
        this.evidenceReader = evidenceReader;
        this.telemetry = telemetry;
    }

    /// <summary>Answers whether a failure says something about every message behind this one rather than about this one.</summary>
    /// <param name="outcome">How a consultation ended.</param>
    /// <returns><see langword="true" /> where asking about the next message would meet the same failure.</returns>
    public static bool ReachesEveryMessage(DecisionModelOutcome outcome) => outcome
        is DecisionModelOutcome.BudgetExhausted
        or DecisionModelOutcome.ProviderUnavailable
        or DecisionModelOutcome.ScannerUnavailable
        or DecisionModelOutcome.NotActivated;

    /// <summary>Asks the whole set of questions about one message.</summary>
    /// <param name="email">The message.</param>
    /// <param name="ownCategories">The mailbox's own categories.</param>
    /// <param name="cancellationToken">Withdraws the call.</param>
    /// <returns>What the message holds, or the reason there is no answer.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public Task<EmailCategoryJudgement> AskAsync(
        EmailAwaitingCategorization email,
        IReadOnlyList<OwnMailCategory> ownCategories,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ownCategories);

        return this.ConsultAsync(
            email,
            (evidence, token) => this.judge.JudgeAsync(evidence, ownCategories, token),
            cancellationToken);
    }

    /// <summary>Asks one own category's question about one message, and nothing else.</summary>
    /// <param name="email">The message.</param>
    /// <param name="category">The category.</param>
    /// <param name="cancellationToken">Withdraws the call.</param>
    /// <returns>That category where the message holds it, nothing where it does not, or the reason there is no answer.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public Task<EmailCategoryJudgement> AskAboutAsync(
        EmailAwaitingCategorization email,
        OwnMailCategory category,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(category);

        return this.ConsultAsync(
            email,
            (evidence, token) => this.judge.JudgeOwnCategoryAsync(evidence, category, token),
            cancellationToken);
    }

    private async Task<EmailCategoryJudgement> ConsultAsync(
        EmailAwaitingCategorization email,
        Func<EmailCategoryEvidence, CancellationToken, Task<EmailCategoryJudgement>> ask,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);

        var content = await this.contentStore.FindStoredContentAsync(email.Id, cancellationToken);
        var message = content is null
            ? null
            : await this.evidenceReader.ReadAsync(email.Id, content, cancellationToken);

        if (message is null)
        {
            this.telemetry.RecordConsultation(DecisionModelOutcome.ContentUnavailable);

            return EmailCategoryJudgement.Unanswered(DecisionModelOutcome.ContentUnavailable);
        }

        var judgement = await ask(new EmailCategoryEvidence(message, email.ConversationOpening), cancellationToken);

        this.telemetry.RecordConsultation(judgement.Outcome);

        return judgement;
    }
}
