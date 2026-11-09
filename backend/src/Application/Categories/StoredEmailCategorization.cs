// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Decisions;
using MailFathom.Application.Persistence;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Categories;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Categories;

/// <summary>Carries the category half of one batch of a run over stored mail, after that batch's spam half has finished.</summary>
/// <remarks>
/// <para>
/// A message that is spam now is not asked about and loses what it held, so a message a run turned into spam keeps nothing;
/// the verdict that decides is the one recorded at this point, whichever stage reached it. Everything else that may be shown
/// to a model is asked about unless it already answers the questions as they stand.
/// </para>
/// <para>
/// <b>A failed consultation leaves that message as it was and the walk goes on.</b> A failure that says something about every
/// message behind this one stops the asking for the rest of the pass rather than being met once per message while the
/// account's run waits, and those messages count as unanswered; a later run costs them one lookup each.
/// </para>
/// <para>
/// One message at a time, because an answer already paid for must not be lost to a provider that stops answering half way
/// through a batch. Identical in every custody phase: it reads stored content, writes local rows, and reaches no mail server.
/// </para>
/// </remarks>
public sealed class StoredEmailCategorization
{
    private readonly IEmailCategoryStore store;
    private readonly IEmailCategoryJudge judge;
    private readonly EmailCategorizationConsultation consultation;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;

    /// <summary>Initializes the use case.</summary>
    /// <param name="store">Reads what may be asked about, and writes down what the model said.</param>
    /// <param name="judge">Says which revisions the questions are asked under now.</param>
    /// <param name="consultation">Reads a message and asks about it.</param>
    /// <param name="egressGuard">Holds the posture the input is scanned under while the batch is asked about.</param>
    /// <param name="commitPolicy">Commits one message's categories, retrying a conflict with a competing writer.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public StoredEmailCategorization(
        IEmailCategoryStore store,
        IEmailCategoryJudge judge,
        EmailCategorizationConsultation consultation,
        SensitiveContentEgressGuard egressGuard,
        OptimisticConcurrencyRetryPolicy commitPolicy)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(judge);
        ArgumentNullException.ThrowIfNull(consultation);
        ArgumentNullException.ThrowIfNull(egressGuard);
        ArgumentNullException.ThrowIfNull(commitPolicy);

        this.store = store;
        this.judge = judge;
        this.consultation = consultation;
        this.egressGuard = egressGuard;
        this.commitPolicy = commitPolicy;
    }

    /// <summary>Removes what spam holds and asks about what is not spam, among the messages of one batch.</summary>
    /// <param name="account">The account.</param>
    /// <param name="emails">The messages of the batch in folders the run asks about.</param>
    /// <param name="terms">What the half is carried under.</param>
    /// <param name="cancellationToken">Cancels between messages; committed categories stay durable.</param>
    /// <returns>What the half did.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when a competing writer wins a race the bounded retries could not resolve.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels, which includes the replica losing the account's lease.</exception>
    public async Task<StoredEmailCategorizationReport> CarryAsync(
        MailAccountId account,
        IReadOnlyList<StoredEmailId> emails,
        StoredEmailCategorizationTerms terms,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(emails);
        ArgumentNullException.ThrowIfNull(terms);

        var clearedEmailCount = await this.commitPolicy.CommitAsync(
            (session, attemptCancellationToken) => this.store.ClearCategoriesOfSpamAsync(
                session,
                account,
                emails,
                terms.OwnCategory,
                attemptCancellationToken),
            cancellationToken);

        var candidates = await this.store.GetStoredEmailsToCategorizeAsync(account, emails, cancellationToken);
        var revisions = this.judge.RevisionsOf(terms.Settings.OwnCategories);
        var askingStoppedBy = terms.AskingStoppedBy;
        var categorizedEmailCount = 0;
        var currentEmailCount = 0;
        var unansweredEmailCount = 0;

        using var actingFor = this.egressGuard.ActingFor(
            await this.egressGuard.ReadPostureOfAsync(account, cancellationToken));

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (terms.OwnCategory is null && !terms.Rescores && AnswersTheCurrentQuestions(candidate.Records, revisions))
            {
                currentEmailCount++;

                continue;
            }

            if (askingStoppedBy is not null)
            {
                unansweredEmailCount++;

                continue;
            }

            var judgement = await this.AskAsync(candidate.Email, terms, cancellationToken);

            if (judgement.Outcome is DecisionModelOutcome.Answered)
            {
                await this.commitPolicy.CommitAsync(
                    (session, attemptCancellationToken) => this.store.ReplaceAsync(
                        session,
                        candidate.Email.Id,
                        judgement.Assignments,
                        terms.OwnCategory,
                        attemptCancellationToken),
                    cancellationToken);

                categorizedEmailCount++;

                continue;
            }

            unansweredEmailCount++;

            if (EmailCategorizationConsultation.ReachesEveryMessage(judgement.Outcome))
            {
                askingStoppedBy = judgement.Outcome;
            }
        }

        return new StoredEmailCategorizationReport(
            categorizedEmailCount,
            currentEmailCount,
            unansweredEmailCount,
            clearedEmailCount,
            askingStoppedBy);
    }

    /// <summary>Answers whether a message's records are answers to the questions as they stand now.</summary>
    /// <remarks>
    /// A yes-or-no answer of <i>no</i> leaves no record, so a message without an own category's record says nothing about
    /// that question and is not asked again here; applying a new or reworded own category to it is what naming one category
    /// on a run is for. A record of a category no longer declared needs nothing either, since it is no longer shown.
    /// </remarks>
    private static bool AnswersTheCurrentQuestions(
        IReadOnlyList<EmailCategoryRecord> records,
        EmailCategoryQuestionRevisions revisions) =>
        records.Any(record => record.Kind is EmailCategoryKind.Default && record.QuestionRevision == revisions.Default)
        && records
            .Where(static record => record.Kind is EmailCategoryKind.Own)
            .All(record => !revisions.Own.TryGetValue(record.Category, out var revision) || revision == record.QuestionRevision);

    private Task<EmailCategoryJudgement> AskAsync(
        EmailAwaitingCategorization email,
        StoredEmailCategorizationTerms terms,
        CancellationToken cancellationToken)
    {
        if (terms.OwnCategory is null)
        {
            return this.consultation.AskAsync(email, terms.Settings.OwnCategories, cancellationToken);
        }

        return terms.Settings.OwnCategoryNamed(terms.OwnCategory) is { } category
            ? this.consultation.AskAboutAsync(email, category, cancellationToken)
            : Task.FromResult(EmailCategoryJudgement.Unanswered(DecisionModelOutcome.NotActivated));
    }
}
