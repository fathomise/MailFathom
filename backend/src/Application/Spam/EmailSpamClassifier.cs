// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam.Gating;
using MailFathom.Application.Spam.Signals;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Spam;

namespace MailFathom.Application.Spam;

/// <summary>Classifies one stored occurrence and records what it concluded.</summary>
/// <remarks>
/// <para>
/// The operation is keyed to the occurrence and is idempotent: repeating it either leaves an existing record alone or
/// replaces it with the same verdict the same inputs produce, so it is safe for a caller to retry and safe for two
/// callers to ask together. The person's own verdict is never replaced, whatever the mode or the terms in force: it is
/// returned as it stands, for the caller to act on. It carries no scheduler, no retry policy of its own, and no
/// execution identity — whatever asks for a classification owns those.
/// </para>
/// <para>
/// It reaches no mail server. Content comes from the local store, which already holds it, so classification never
/// triggers an IMAP fetch and cannot affect a remote <c>\Seen</c> flag. It writes no folder, no flag, no keyword, and
/// nothing about where the message lives; the keywords it reads are the ones the last synchronization stored.
/// </para>
/// <para>
/// It writes one thing besides the classification, and only when the verdict is spam: everything derived from the
/// message's own content is removed — the passages cut from it, the vectors hanging off them, and the readings stored
/// for its attachments. Ordering classification ahead of chunking and attachment reading is what stops them being
/// created for junk in the first place, so this reaches only mail that was derived before anybody scored it — the case
/// an on-demand run over an existing mailbox produces and the arrival path never does.
/// </para>
/// <para>
/// Nothing it reads is loggable. The occurrence identifier, the folder alias, the outcome, and the verdict are safe to
/// report; a header value, an authentication detail, and a subject are not, and none of the three reaches a log line
/// from here.
/// </para>
/// </remarks>
public sealed class EmailSpamClassifier
{
    private readonly IClassifiableEmailReader emailReader;
    private readonly IEmailContentStore contentStore;
    private readonly IEmailSpamHeaderReader headerReader;
    private readonly IJunkMailFolderCatalog junkFolders;
    private readonly DeterministicSpamClassifier deterministicClassifier;
    private readonly ISpamClassificationSettingsReader settingsReader;
    private readonly IEmailSpamClassificationStore classificationStore;
    private readonly ISpamVerdictActStore verdictActs;
    private readonly IEmailChunkStore chunkStore;
    private readonly IStoredEmailAttachmentTextStore attachmentTextStore;
    private readonly IDerivedWorkGateTelemetry gateTelemetry;
    private readonly OptimisticConcurrencyRetryPolicy retryPolicy;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the use case.</summary>
    /// <param name="emailReader">Finds the folder of the email, within the account that holds it.</param>
    /// <param name="contentStore">Reads the raw MIME already stored for it.</param>
    /// <param name="headerReader">Reads the spam-relevant headers out of that content.</param>
    /// <param name="junkFolders">Answers whether the occurrence's folder is its account's junk folder.</param>
    /// <param name="deterministicClassifier">Reaches a verdict from what the message already carried.</param>
    /// <param name="settingsReader">Answers what the occurrence's account decided about the mail in it.</param>
    /// <param name="classificationStore">Records the classification.</param>
    /// <param name="verdictActs">Says what this deployment's verdicts already did to the message, which is not evidence.</param>
    /// <param name="chunkStore">Removes the passages and vectors of a message the verdict calls junk.</param>
    /// <param name="attachmentTextStore">Removes the attachment readings of a message the verdict calls junk.</param>
    /// <param name="gateTelemetry">Counts what a junk verdict had to remove, without describing any of it.</param>
    /// <param name="retryPolicy">Commits the record from a fresh read when a concurrent write conflicts.</param>
    /// <param name="timeProvider">Stamps the evaluation time.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public EmailSpamClassifier(
        IClassifiableEmailReader emailReader,
        IEmailContentStore contentStore,
        IEmailSpamHeaderReader headerReader,
        IJunkMailFolderCatalog junkFolders,
        DeterministicSpamClassifier deterministicClassifier,
        ISpamClassificationSettingsReader settingsReader,
        IEmailSpamClassificationStore classificationStore,
        ISpamVerdictActStore verdictActs,
        IEmailChunkStore chunkStore,
        IStoredEmailAttachmentTextStore attachmentTextStore,
        IDerivedWorkGateTelemetry gateTelemetry,
        OptimisticConcurrencyRetryPolicy retryPolicy,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(emailReader);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(headerReader);
        ArgumentNullException.ThrowIfNull(junkFolders);
        ArgumentNullException.ThrowIfNull(deterministicClassifier);
        ArgumentNullException.ThrowIfNull(settingsReader);
        ArgumentNullException.ThrowIfNull(classificationStore);
        ArgumentNullException.ThrowIfNull(verdictActs);
        ArgumentNullException.ThrowIfNull(chunkStore);
        ArgumentNullException.ThrowIfNull(attachmentTextStore);
        ArgumentNullException.ThrowIfNull(gateTelemetry);
        ArgumentNullException.ThrowIfNull(retryPolicy);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.emailReader = emailReader;
        this.contentStore = contentStore;
        this.headerReader = headerReader;
        this.junkFolders = junkFolders;
        this.deterministicClassifier = deterministicClassifier;
        this.settingsReader = settingsReader;
        this.classificationStore = classificationStore;
        this.verdictActs = verdictActs;
        this.chunkStore = chunkStore;
        this.attachmentTextStore = attachmentTextStore;
        this.gateTelemetry = gateTelemetry;
        this.retryPolicy = retryPolicy;
        this.timeProvider = timeProvider;
    }

    /// <summary>Classifies one occurrence, on the terms decided for the mailbox it belongs to.</summary>
    /// <param name="account">
    /// The account whose posture decides everything below. The email is read only within this account's mail, so an account which
    /// does not hold it finds nothing and the attempt ends as <see cref="SpamClassificationOutcome.OccurrenceMissing" />.
    /// </param>
    /// <param name="emailId">The occurrence to classify.</param>
    /// <param name="mode">What to do about an occurrence that already carries a classification.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>What was recorded, or the reason nothing was.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="mode" /> is not a defined member.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when every allowed commit attempt conflicted.</exception>
    /// <remarks>
    /// The order of the checks is the order of what they cost. Whether this account is classified at all is free, then the email
    /// is looked up within the account's own mail — an email the account does not hold ends here, before anything else about it
    /// is read — then the scope and the existing record are one lookup each, and only then is content read, so an
    /// occurrence outside the scope costs no read of its mail, which is the property that keeps a switched-off feature
    /// free.
    /// </remarks>
    public async Task<SpamClassificationResult> ClassifyAsync(
        MailAccountId account,
        StoredEmailId emailId,
        SpamClassificationMode mode,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "A classification either leaves an existing record alone or replaces it.");
        }

        var settings = this.settingsReader.SettingsFor(account);

        if (!settings.IsEnabled)
        {
            return SpamClassificationResult.NotClassified(SpamClassificationOutcome.Disabled);
        }

        var email = await this.emailReader.FindAsync(account, emailId, cancellationToken);

        if (email is null)
        {
            return SpamClassificationResult.NotClassified(SpamClassificationOutcome.OccurrenceMissing);
        }

        if (!settings.Covers(email.FolderAlias))
        {
            return SpamClassificationResult.NotClassified(SpamClassificationOutcome.OutsideConfiguredScope);
        }

        var existing = await this.classificationStore.FindAsync(emailId, cancellationToken);

        // Read here so a person's verdict costs no read of the message. It is not what protects that verdict: the write
        // below is conditional on the row it would replace, which catches one recorded after this read.
        if (existing is { DecidedBy: SpamClassificationStage.Person })
        {
            return SpamClassificationResult.PersonsVerdictKept(existing);
        }

        if (mode is SpamClassificationMode.FirstTimeOnly && existing is not null)
        {
            return SpamClassificationResult.NotClassified(SpamClassificationOutcome.AlreadyClassified);
        }

        var content = await this.contentStore.FindStoredContentAsync(emailId, cancellationToken);

        if (content is null)
        {
            return SpamClassificationResult.NotClassified(SpamClassificationOutcome.ContentUnavailable);
        }

        var classification = await this.EvaluateAsync(email, settings, content, cancellationToken);
        var discardedPassageCount = 0;
        var written = false;

        await this.retryPolicy.CommitAsync(
            async (session, attemptCancellationToken) =>
            {
                written = await this.classificationStore.SaveAsync(session, classification, attemptCancellationToken);

                if (!written)
                {
                    return;
                }

                // In the same transaction as the verdict that calls for it, so a crash never leaves a message recorded
                // as junk while what was derived from it is still retrievable. An attempt the retry policy replays
                // reports what that attempt removed rather than the sum of every attempt, which is why the count is
                // assigned rather than accumulated.
                //
                // The attachment readings go with the passages rather than after them: a reading is a second durable
                // copy of the same words, so removing only what was cut from it would leave the document's whole text
                // and the description of its pictures exactly where the verdict called for their removal.
                discardedPassageCount = classification.Verdict is SpamVerdict.Spam
                    ? await this.chunkStore.DiscardChunksAsync(session, emailId, attemptCancellationToken)
                    : 0;

                if (classification.Verdict is SpamVerdict.Spam)
                {
                    await this.attachmentTextStore.DiscardAttachmentTextAsync(
                        session,
                        emailId,
                        attemptCancellationToken);
                }
            },
            cancellationToken);

        if (!written)
        {
            return await this.classificationStore.FindAsync(emailId, cancellationToken) is { } kept
                ? SpamClassificationResult.PersonsVerdictKept(kept)
                : SpamClassificationResult.NotClassified(SpamClassificationOutcome.OccurrenceMissing);
        }

        if (classification.Verdict is SpamVerdict.Spam)
        {
            this.gateTelemetry.RecordDiscardedPassages(discardedPassageCount);
        }

        return SpamClassificationResult.Classified(classification);
    }

    /// <summary>Reads one occurrence and composes the record its verdict is kept as.</summary>
    private async Task<SpamClassification> EvaluateAsync(
        ClassifiableEmail email,
        SpamClassificationSettings settings,
        StoredEmailContent content,
        CancellationToken cancellationToken)
    {
        var facts = await this.headerReader.ReadAsync(content, cancellationToken);
        var ownActs = await this.verdictActs.FindAsync(email.Id, cancellationToken);
        var reading = this.deterministicClassifier.Read(
            facts,
            email.FolderAlias,
            this.junkFolders.IsJunkFolder(email.AccountId, email.FolderAlias),
            email.Keywords,
            ownActs);

        return SpamClassification.Create(
            email.Id,
            reading.Verdict,
            SpamClassificationStage.Deterministic,
            reading.Assessment,
            settings.Profile,
            reading.Signals,
            this.timeProvider.GetUtcNow());
    }
}
