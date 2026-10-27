// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam.Gating;
using MailFathom.Application.Spam.Runs;
using MailFathom.Application.Spam.Signals;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;

namespace MailFathom.Application.Spam.Floor;

/// <summary>Gives every message of one account that sits in its junk folder or carries <c>$Junk</c> at least a spam verdict.</summary>
/// <remarks>
/// <para>
/// It is a floor beneath classification rather than a widening of it. It reads where a message is and which keywords it
/// carries, nothing of its content, and it needs no classification switched on and no folder in the classified set: a
/// message below the floor is given a spam verdict with no subcategory, recorded as the deterministic stage with the
/// placement or the keyword as its signal. What the mail server is told stays with the account's switches, so the pass
/// writes nothing to it, and it raises the verdict alone.
/// </para>
/// <para>
/// It is a step of the account's synchronization run, so a message reaches it in the run that read it from the server —
/// a new arrival, a message another client moved into the junk folder, a keyword another client set — and the run after
/// an upgrade reaches the mail stored before the floor existed. A message it raised is no candidate afterwards, so a
/// mailbox already at the floor costs one bounded query per run. A held account is read the same way, against its local
/// folders and its stored keywords.
/// </para>
/// <para>
/// A second replica finding the work done is the ordinary case rather than a race: the write is conditional on the row
/// holding neither spam nor the person's verdict when the session reads it, and a row written after that read
/// conflicts and is read again, so two passes over one message leave one verdict.
/// </para>
/// </remarks>
public sealed class SpamVerdictFloorPass
{
    private readonly ISpamVerdictFloorCandidateReader candidates;
    private readonly ISpamClassificationSettingsReader settingsReader;
    private readonly IDeploymentMailFolders deploymentFolders;
    private readonly ISpamVerdictActStore verdictActs;
    private readonly DeterministicSpamClassifier deterministicClassifier;
    private readonly IEmailSpamClassificationStore classifications;
    private readonly IEmailChunkStore chunkStore;
    private readonly IStoredEmailAttachmentTextStore attachmentTextStore;
    private readonly IDerivedWorkGateTelemetry gateTelemetry;
    private readonly OptimisticConcurrencyRetryPolicy retryPolicy;
    private readonly SpamClassificationRunOptions options;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the pass over the mail it reads and the record it raises.</summary>
    /// <param name="candidates">Finds the account's mail below the floor.</param>
    /// <param name="settingsReader">Answers whether the account classifies, over which folders, and how long a verdict may take.</param>
    /// <param name="deploymentFolders">Answers which of the account's folders is its junk folder.</param>
    /// <param name="verdictActs">Says what this deployment's verdicts already did to a message, which decides whether its <c>$NotJunk</c> stands alone.</param>
    /// <param name="deterministicClassifier">Decides what the floor raises a message to.</param>
    /// <param name="classifications">Records the raised verdict.</param>
    /// <param name="chunkStore">Removes the passages of a message raised on an account that classifies.</param>
    /// <param name="attachmentTextStore">Removes the attachment readings of such a message.</param>
    /// <param name="gateTelemetry">Counts the passages removed, without describing any of them.</param>
    /// <param name="retryPolicy">Commits each verdict from a fresh read when a concurrent write conflicts.</param>
    /// <param name="options">Bounds one pass, with the width a classification run's pass has.</param>
    /// <param name="timeProvider">Stamps the evaluation time and measures the classification wait.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public SpamVerdictFloorPass(
        ISpamVerdictFloorCandidateReader candidates,
        ISpamClassificationSettingsReader settingsReader,
        IDeploymentMailFolders deploymentFolders,
        ISpamVerdictActStore verdictActs,
        DeterministicSpamClassifier deterministicClassifier,
        IEmailSpamClassificationStore classifications,
        IEmailChunkStore chunkStore,
        IStoredEmailAttachmentTextStore attachmentTextStore,
        IDerivedWorkGateTelemetry gateTelemetry,
        OptimisticConcurrencyRetryPolicy retryPolicy,
        SpamClassificationRunOptions options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(settingsReader);
        ArgumentNullException.ThrowIfNull(deploymentFolders);
        ArgumentNullException.ThrowIfNull(verdictActs);
        ArgumentNullException.ThrowIfNull(deterministicClassifier);
        ArgumentNullException.ThrowIfNull(classifications);
        ArgumentNullException.ThrowIfNull(chunkStore);
        ArgumentNullException.ThrowIfNull(attachmentTextStore);
        ArgumentNullException.ThrowIfNull(gateTelemetry);
        ArgumentNullException.ThrowIfNull(retryPolicy);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.candidates = candidates;
        this.settingsReader = settingsReader;
        this.deploymentFolders = deploymentFolders;
        this.verdictActs = verdictActs;
        this.deterministicClassifier = deterministicClassifier;
        this.classifications = classifications;
        this.chunkStore = chunkStore;
        this.attachmentTextStore = attachmentTextStore;
        this.gateTelemetry = gateTelemetry;
        this.retryPolicy = retryPolicy;
        this.options = options;
        this.timeProvider = timeProvider;
    }

    /// <summary>Raises the account's mail below the floor, a bounded number of batches at a time.</summary>
    /// <param name="account">The account whose mail is raised.</param>
    /// <param name="cancellationToken">Cancels the pass between and within batches.</param>
    /// <returns>How many messages this pass raised to spam.</returns>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when every allowed commit attempt for one message conflicted.</exception>
    /// <remarks>What one pass leaves, because it reached its bound, the next run's pass reaches.</remarks>
    public async Task<int> RunAsync(MailAccountId account, CancellationToken cancellationToken)
    {
        var settings = this.settingsReader.SettingsFor(account);
        var terms = await this.ReadTermsAsync(account, settings, cancellationToken);
        var raised = 0;
        var resumeAfter = default(StoredEmailId?);

        for (var batch = 0; batch < this.options.MaxBatchesPerPass; batch++)
        {
            var found = await this.candidates.GetCandidatesAsync(
                account,
                terms,
                resumeAfter,
                this.options.BatchSize,
                cancellationToken);

            foreach (var candidate in found)
            {
                if (await this.LiftAsync(candidate, terms, settings, cancellationToken))
                {
                    raised++;
                }
            }

            if (found.Count < this.options.BatchSize)
            {
                break;
            }

            resumeAfter = found[^1].Id;
        }

        return raised;
    }

    private async Task<SpamVerdictFloorTerms> ReadTermsAsync(
        MailAccountId account,
        SpamClassificationSettings settings,
        CancellationToken cancellationToken)
    {
        var junkFolders = await this.deploymentFolders.ReadAsync(
            MailFolderSelection.Junk,
            [account],
            cancellationToken);
        var scope = await this.settingsReader.ReadScopeInForceAsync(cancellationToken);

        return new SpamVerdictFloorTerms(
            [.. junkFolders.Where(folder => folder.AccountId == account).Select(static folder => folder.Alias)],
            settings.IsEnabled ? settings.ScannedFolderAliases : [],
            this.timeProvider.GetUtcNow() - scope.MaximumClassificationWait);
    }

    /// <summary>Raises one candidate, reporting whether a verdict was written.</summary>
    /// <remarks>
    /// The rule is decided here as well as in the candidates' query, so what the record says is what the deterministic
    /// stage says about the floor rather than what a query happened to select. The profile is the deterministic stage's
    /// own rather than the account's: a classification run under terms that consult a model reaches the message again
    /// and may name a subcategory above the floor, while a run under the deterministic terms counts it as decided.
    /// </remarks>
    private async Task<bool> LiftAsync(
        SpamVerdictFloorCandidate candidate,
        SpamVerdictFloorTerms terms,
        SpamClassificationSettings settings,
        CancellationToken cancellationToken)
    {
        var ownActs = await this.verdictActs.FindAsync(candidate.Id, cancellationToken);
        var junkFolder = candidate.FolderAlias is { } alias && terms.JunkFolders.Contains(alias) ? (MailFolderAlias?)alias : null;
        var reading = this.deterministicClassifier.LiftToFloor(
            DeterministicSpamReading.Nothing,
            junkFolder,
            candidate.Keywords,
            ownActs);

        if (reading.Verdict is not SpamVerdict.Spam)
        {
            return false;
        }

        var classification = SpamClassification.Create(
            candidate.Id,
            SpamVerdict.Spam,
            SpamClassificationStage.Deterministic,
            assessment: null,
            SpamClassificationProfile.Create(),
            reading.Signals,
            this.timeProvider.GetUtcNow());
        var written = false;
        var discardedPassageCount = 0;

        await this.retryPolicy.CommitAsync(
            async (session, attemptCancellationToken) =>
            {
                written = await this.classifications.LiftToSpamAsync(session, classification, attemptCancellationToken);

                // Only where the account classifies, because that is where junk is kept out of what is derived: elsewhere
                // nothing gates the cut, so passages removed here would be cut again and removed for nothing.
                if (!written || !settings.IsEnabled)
                {
                    discardedPassageCount = 0;

                    return;
                }

                discardedPassageCount = await this.chunkStore.DiscardChunksAsync(
                    session,
                    candidate.Id,
                    attemptCancellationToken);
                await this.attachmentTextStore.DiscardAttachmentTextAsync(
                    session,
                    candidate.Id,
                    attemptCancellationToken);
            },
            cancellationToken);

        if (written && settings.IsEnabled)
        {
            this.gateTelemetry.RecordDiscardedPassages(discardedPassageCount);
        }

        return written;
    }
}
