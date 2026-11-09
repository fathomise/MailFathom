// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Decisions;
using MailFathom.Application.Persistence;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Categories;

/// <summary>Asks a decision model about each arriving message of one account, and writes down what it said.</summary>
/// <remarks>
/// <para>
/// A stage of the account's run, after the spam verdict and before the rule pass. <b>It reaches arriving mail alone</b>:
/// the store selects what no rule pass has evaluated yet, so switching categorization on re-reads nothing already
/// stored, and a run over stored mail stays the explicit request it is. It walks as far as the rule pass behind it does,
/// so no message passes that stage without having been asked about.
/// </para>
/// <para>
/// <b>It fails open.</b> An unreachable provider, a spent budget, or an unreadable answer leaves a message with no
/// category. Only a rule pass whose rules name the category fact waits for one, and for no longer than a spam verdict may
/// take, so the message still settles; it is not asked about again once that pass has evaluated it. A failure that says
/// something about every message behind it ends the pass rather than being met once per message, because this stage
/// holds the account's run while it asks.
/// </para>
/// <para>
/// Identical in every custody phase, so nothing here reads one: it reads stored content, writes local rows, reaches no
/// mail server, and needs no custodian.
/// </para>
/// <para>
/// A replica that loses the account's lease has this pass cancelled between messages, and what it committed stays. One
/// whose call was already in flight commits nothing over the answer of the replica that took the account: the store
/// refuses a second set of categories for one message.
/// </para>
/// <para>
/// No client is told. The message has not settled while this stage reaches it, so no read serves it yet, and the signal
/// that settles it carries what this wrote.
/// </para>
/// </remarks>
public sealed class EmailCategorizationPass
{
    private readonly IEmailCategoryStore store;
    private readonly IEmailCategoryJudge judge;
    private readonly IEmailCategorizationSettingsReader settingsReader;
    private readonly EmailCategorizationConsultation consultation;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly IEmailCategorizationTelemetry telemetry;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly MailRuleEvaluationOptions walkBounds;

    /// <summary>Initializes the pass.</summary>
    /// <param name="store">Reads the mail awaiting categories and writes down what the model said.</param>
    /// <param name="judge">Asks the model.</param>
    /// <param name="settingsReader">Answers what the account decided.</param>
    /// <param name="consultation">Reads a message and asks about it.</param>
    /// <param name="egressGuard">Holds the posture the input is scanned under while the pass runs.</param>
    /// <param name="telemetry">Counts how each consultation ended.</param>
    /// <param name="commitPolicy">Commits one message's categories, retrying a conflict with a competing writer.</param>
    /// <param name="walkBounds">The rule pass's own bounds, so this stage reaches every message that one will.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a configured bound is below one.</exception>
    public EmailCategorizationPass(
        IEmailCategoryStore store,
        IEmailCategoryJudge judge,
        IEmailCategorizationSettingsReader settingsReader,
        EmailCategorizationConsultation consultation,
        SensitiveContentEgressGuard egressGuard,
        IEmailCategorizationTelemetry telemetry,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        MailRuleEvaluationOptions walkBounds)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(judge);
        ArgumentNullException.ThrowIfNull(settingsReader);
        ArgumentNullException.ThrowIfNull(consultation);
        ArgumentNullException.ThrowIfNull(egressGuard);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(walkBounds);
        ArgumentOutOfRangeException.ThrowIfLessThan(walkBounds.BatchSize, 1, nameof(walkBounds));
        ArgumentOutOfRangeException.ThrowIfLessThan(walkBounds.MaxBatchesPerPass, 1, nameof(walkBounds));

        this.store = store;
        this.judge = judge;
        this.settingsReader = settingsReader;
        this.consultation = consultation;
        this.egressGuard = egressGuard;
        this.telemetry = telemetry;
        this.commitPolicy = commitPolicy;
        this.walkBounds = walkBounds;
    }

    /// <summary>Takes one bounded pass over the account's arriving mail.</summary>
    /// <param name="account">The account whose mail is categorized.</param>
    /// <param name="cancellationToken">Cancels the pass between messages; committed categories stay durable.</param>
    /// <returns>What the pass did, and what stopped it where something did.</returns>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when a competing writer wins a race the bounded retries could not resolve.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels, which includes the replica losing the account's lease.</exception>
    public async Task<EmailCategorizationPassReport> RunAsync(MailAccountId account, CancellationToken cancellationToken)
    {
        var settings = this.settingsReader.SettingsFor(account);

        if (!settings.IsEnabled)
        {
            return EmailCategorizationPassReport.Nothing;
        }

        if (!this.judge.IsActive)
        {
            this.telemetry.RecordConsultation(DecisionModelOutcome.NotActivated);

            return EmailCategorizationPassReport.Nothing with { StoppedBy = DecisionModelOutcome.NotActivated };
        }

        using var actingFor = this.egressGuard.ActingFor(
            await this.egressGuard.ReadPostureOfAsync(account, cancellationToken));

        var categorizedCount = 0;
        var unansweredCount = 0;
        StoredEmailId? position = null;

        for (var batchNumber = 1; batchNumber <= this.walkBounds.MaxBatchesPerPass; batchNumber++)
        {
            var batch = await this.store.GetEmailsAwaitingCategorizationAsync(
                account,
                position,
                this.walkBounds.BatchSize,
                cancellationToken);

            foreach (var email in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var outcome = await this.CategorizeAsync(email, settings, cancellationToken);

                if (outcome is DecisionModelOutcome.Answered)
                {
                    categorizedCount++;

                    continue;
                }

                unansweredCount++;

                if (EmailCategorizationConsultation.ReachesEveryMessage(outcome))
                {
                    return new EmailCategorizationPassReport(categorizedCount, unansweredCount, outcome, EmailsRemain: true);
                }
            }

            if (batch.Count < this.walkBounds.BatchSize)
            {
                return new EmailCategorizationPassReport(categorizedCount, unansweredCount, StoppedBy: null, EmailsRemain: false);
            }

            position = batch[^1].Id;
        }

        return new EmailCategorizationPassReport(categorizedCount, unansweredCount, StoppedBy: null, EmailsRemain: true);
    }

    /// <summary>Asks about one message and commits what came back, reporting how the consultation ended.</summary>
    private async Task<DecisionModelOutcome> CategorizeAsync(
        EmailAwaitingCategorization email,
        EmailCategorizationSettings settings,
        CancellationToken cancellationToken)
    {
        var judgement = await this.consultation.AskAsync(email, settings.OwnCategories, cancellationToken);

        if (judgement.Outcome is DecisionModelOutcome.Answered)
        {
            // One message at a time, because an answer already paid for must not be lost to a provider that stops
            // answering half way through a batch. A second replica's answer standing first is not a failure.
            await this.commitPolicy.CommitAsync(
                (session, attemptCancellationToken) => this.store.SaveAsync(
                    session,
                    email.Id,
                    judgement.Assignments,
                    attemptCancellationToken),
                cancellationToken);
        }

        return judgement.Outcome;
    }
}
