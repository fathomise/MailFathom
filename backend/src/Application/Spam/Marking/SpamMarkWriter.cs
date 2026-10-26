// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Authoring.Failures;
using MailFathom.Application.Persistence;
using MailFathom.Application.Rules.Actions;
using MailFathom.Application.Spam.Gating;
using MailFathom.Application.Spam.Signals;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Spam;

namespace MailFathom.Application.Spam.Marking;

/// <summary>Writes the person's own spam verdict and the junk keywords that follow it, inside the transaction of the act that made it.</summary>
/// <remarks>
/// <para>
/// Two acts make the person's verdict: marking messages spam or not spam, and moving a message into the account's junk
/// folder, which is the same act as marking it spam. Each owns its own transaction and whatever move it carries, and
/// both write the verdict through this, so the verdict, what it withholds, and the keywords other mail clients read it
/// by are one decision recorded the same way whichever act made it.
/// </para>
/// <para>
/// The keywords are the ones RFC 9051 defines. Spam adds <c>$Junk</c>, with <c>$Phishing</c> beside it for the
/// phishing subcategory, and takes off <c>$NotJunk</c>; not spam adds <c>$NotJunk</c> and takes off <c>$Junk</c> and
/// <c>$Phishing</c>. Every mark asks for all of them rather than for what the server last reported missing, as every
/// keyword change a person asks for does: a mark undone before the first one reached the server is then corrected by
/// the records of the second, which converge after it, instead of being judged against a reading the first one has not
/// changed yet. They are the person's own keyword acts: the account's <c>WriteKeywords</c> permission governs them
/// as it governs every keyword MailFathom writes, and the switch letting an automated verdict write keywords does not.
/// Each travels as every change on a message does, by the custodian the account's phase selects.
/// </para>
/// <para>
/// A keyword this writes is recorded as an act of a verdict of this deployment, so the action an operator's switches
/// take on a verdict never writes it a second time after somebody took it off. Nothing about the message is logged or
/// counted here but how many passages a spam mark removed.
/// </para>
/// </remarks>
public sealed class SpamMarkWriter
{
    private readonly IMailRuleActionPermissionReader permissions;
    private readonly IEmailSpamClassificationStore classifications;
    private readonly ISpamVerdictActStore verdictActs;
    private readonly IEmailChunkStore chunks;
    private readonly IStoredEmailAttachmentTextStore attachmentTexts;
    private readonly IDerivedWorkGateTelemetry gateTelemetry;
    private readonly MailboxChangeSubmission submission;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the writer over what a mark reads and the stores it writes.</summary>
    /// <param name="permissions">Answers whether the account permits keywords to be written.</param>
    /// <param name="classifications">Reads the person's earlier verdict and records the new one.</param>
    /// <param name="verdictActs">Records the keywords a mark wrote and the filing it made.</param>
    /// <param name="chunks">Removes the passages and vectors of a message the person calls spam.</param>
    /// <param name="attachmentTexts">Removes the attachment readings of a message the person calls spam.</param>
    /// <param name="gateTelemetry">Counts what a spam mark removed, without describing any of it.</param>
    /// <param name="submission">Records or commits each keyword change through the account's custodian.</param>
    /// <param name="timeProvider">Stamps the verdict.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public SpamMarkWriter(
        IMailRuleActionPermissionReader permissions,
        IEmailSpamClassificationStore classifications,
        ISpamVerdictActStore verdictActs,
        IEmailChunkStore chunks,
        IStoredEmailAttachmentTextStore attachmentTexts,
        IDerivedWorkGateTelemetry gateTelemetry,
        MailboxChangeSubmission submission,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(classifications);
        ArgumentNullException.ThrowIfNull(verdictActs);
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(attachmentTexts);
        ArgumentNullException.ThrowIfNull(gateTelemetry);
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.permissions = permissions;
        this.classifications = classifications;
        this.verdictActs = verdictActs;
        this.chunks = chunks;
        this.attachmentTexts = attachmentTexts;
        this.gateTelemetry = gateTelemetry;
        this.submission = submission;
        this.timeProvider = timeProvider;
    }

    /// <summary>Works out the junk keywords a mark adds and takes off, before the transaction it is written in opens.</summary>
    /// <param name="account">The account the message belongs to, whose permission governs the keywords.</param>
    /// <param name="mark">What the person says the message is.</param>
    /// <returns>The mark with the keywords it adds and takes off, either of which is <see langword="null" /> where the account permits nothing.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="mark" /> is <see langword="null" />.</exception>
    public PreparedSpamMark Prepare(MailAccountId account, SpamMark mark)
    {
        ArgumentNullException.ThrowIfNull(mark);

        var isSpam = mark.Verdict is SpamVerdict.Spam;
        string[] wanted = isSpam
            ? mark.Subcategory is SpamSubcategory.Phishing ? [JunkKeywords.Junk, JunkKeywords.Phishing] : [JunkKeywords.Junk]
            : [JunkKeywords.NotJunk];
        string[] unwanted = isSpam ? [JunkKeywords.NotJunk] : [JunkKeywords.Junk, JunkKeywords.Phishing];

        return new PreparedSpamMark(
            mark,
            this.KeywordsPermitted(account, MailboxMutation.AddKeywords, wanted),
            this.KeywordsPermitted(account, MailboxMutation.RemoveKeywords, unwanted));
    }

    /// <summary>Works out the mark a move into the account's junk folder makes, which keeps the kind of spam the person already named.</summary>
    /// <param name="emailId">The message being moved.</param>
    /// <param name="account">The account the message belongs to.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>A spam mark with the subcategory of the person's own earlier spam verdict, or with none.</returns>
    /// <remarks>
    /// The move names no subcategory, so it takes none away: a message the person already called phishing stays phishing
    /// when they file it, and one they had called not spam becomes spam with no subcategory until they name one.
    /// </remarks>
    public async Task<PreparedSpamMark> PrepareMoveIntoJunkAsync(
        StoredEmailId emailId,
        MailAccountId account,
        CancellationToken cancellationToken)
    {
        var existing = await this.classifications.FindAsync(emailId, cancellationToken);
        var named = existing is { DecidedBy: SpamClassificationStage.Person, Verdict: SpamVerdict.Spam }
            ? existing.Subcategory
            : null;

        return this.Prepare(account, SpamMark.Spam(named));
    }

    /// <summary>Records the person's verdict, removes what a spam mark withholds, and writes the keywords, inside the caller's transaction.</summary>
    /// <param name="session">The transaction the act commits in.</param>
    /// <param name="emailId">The message.</param>
    /// <param name="occurrence">Where the message is on the mail server, which a keyword change is issued against.</param>
    /// <param name="prepared">The mark and the keywords it owes.</param>
    /// <param name="requester">The invocation asking, which a keyword record is identified by.</param>
    /// <param name="cancellationToken">Cancels the writes.</param>
    /// <returns>The records opened, the local changes to announce once the transaction commits, and how many passages were removed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="AuthoredMailChangeTargetNotFoundException">Thrown when a held account no longer stores the message, so the transaction never commits.</exception>
    /// <exception cref="MailFlagChangeInvalidException">Thrown when the requester identity already asked for other keywords on this message, so the transaction never commits.</exception>
    /// <remarks>
    /// The keywords are written before whatever move the caller makes afterwards in the same transaction, because on a
    /// server without RFC 6851 <c>MOVE</c> a relocation gives the message a new UID and a keyword stored after it would be
    /// aimed at an occurrence the source folder no longer holds.
    /// </remarks>
    public async Task<StagedSpamMark> StageAsync(
        IPersistenceSession session,
        StoredEmailId emailId,
        EmailOccurrenceId occurrence,
        PreparedSpamMark prepared,
        MailboxMutationRequester requester,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(requester);

        var mark = prepared.Mark;

        await this.classifications.SaveAsync(
            session,
            SpamClassification.Create(
                emailId,
                mark.Verdict,
                SpamClassificationStage.Person,
                assessment: null,
                profile: default,
                signals: [],
                this.timeProvider.GetUtcNow(),
                mark.Subcategory),
            cancellationToken);

        var discardedPassages = 0;

        if (mark.Verdict is SpamVerdict.Spam)
        {
            discardedPassages = await this.chunks.DiscardChunksAsync(session, emailId, cancellationToken);
            await this.attachmentTexts.DiscardAttachmentTextAsync(session, emailId, cancellationToken);
        }

        var records = new List<MailboxMutationRecord>(capacity: 2);
        var applied = new List<AppliedMailboxChange>(capacity: 2);

        if (prepared.Additions is { } additions)
        {
            await this.SubmitKeywordsAsync(
                session,
                MailboxMutationRequest.AddKeywords(emailId, occurrence, requester, additions),
                records,
                applied,
                cancellationToken);

            await this.RecordJunkKeywordActsAsync(session, emailId, mark, additions, cancellationToken);
        }

        if (prepared.Removals is { } removals)
        {
            await this.SubmitKeywordsAsync(
                session,
                MailboxMutationRequest.RemoveKeywords(emailId, occurrence, requester, removals),
                records,
                applied,
                cancellationToken);
        }

        return new StagedSpamMark(records, applied, discardedPassages);
    }

    /// <summary>Records that the person's spam mark filed the message, so no verdict files it again once somebody moves it back out.</summary>
    /// <param name="session">The transaction the filing commits in.</param>
    /// <param name="emailId">The message.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the act is staged.</returns>
    public Task RecordFiledIntoJunkAsync(
        IPersistenceSession session,
        StoredEmailId emailId,
        CancellationToken cancellationToken) =>
        this.verdictActs.RecordAsync(session, emailId, SpamVerdictAct.FiledIntoJunk, cancellationToken);

    /// <summary>Tells the clients watching the account what a committed mark changed locally, and counts what it removed.</summary>
    /// <param name="staged">What the committed transaction staged.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="staged" /> is <see langword="null" />.</exception>
    /// <remarks>Called only once the transaction has committed, for the reason <see cref="MailboxChangeSubmission.Announce" /> gives.</remarks>
    public void Announce(StagedSpamMark staged)
    {
        ArgumentNullException.ThrowIfNull(staged);

        foreach (var change in staged.Applied)
        {
            this.submission.Announce(change);
        }

        if (staged.DiscardedPassageCount > 0)
        {
            this.gateTelemetry.RecordDiscardedPassages(staged.DiscardedPassageCount);
        }
    }

    /// <summary>Records the junk keywords a spam mark wrote, so neither is read back as somebody else's signal or written again by a verdict.</summary>
    /// <remarks>A not-spam mark records none: <c>$NotJunk</c> is the person's correction wherever it stands, never an act to discount.</remarks>
    private async Task RecordJunkKeywordActsAsync(
        IPersistenceSession session,
        StoredEmailId emailId,
        SpamMark mark,
        AuthoredMailKeywords additions,
        CancellationToken cancellationToken)
    {
        if (mark.Verdict is not SpamVerdict.Spam)
        {
            return;
        }

        foreach (var act in additions.Values.Select(ActOf))
        {
            await this.verdictActs.RecordAsync(session, emailId, act, cancellationToken);
        }
    }

    private static SpamVerdictAct ActOf(string keyword) => keyword == JunkKeywords.Phishing
        ? SpamVerdictAct.MarkedPhishing
        : SpamVerdictAct.MarkedJunk;

    /// <summary>Submits one keyword change, keeping what it wrote, and refuses the whole act where it could not be made.</summary>
    private async Task SubmitKeywordsAsync(
        IPersistenceSession session,
        MailboxMutationRequest request,
        List<MailboxMutationRecord> records,
        List<AppliedMailboxChange> applied,
        CancellationToken cancellationToken)
    {
        var submitted = await this.submission.SubmitAsync(
            session,
            request,
            destination: null,
            heldUntil: null,
            cancellationToken);

        if (submitted.Outcome is not (MailboxChangeSubmissionOutcome.Recorded or MailboxChangeSubmissionOutcome.Applied))
        {
            throw new AuthoredMailChangeTargetNotFoundException();
        }

        if (submitted.Record is { } record)
        {
            if (record.Request.Keywords != request.Keywords)
            {
                throw MailFlagChangeInvalidException.RequestIdAlreadyAskedForAnother();
            }

            records.Add(record);
        }

        if (submitted.Change is { } change)
        {
            applied.Add(change);
        }
    }

    /// <summary>Keeps the keywords the account permits the mutation for, or <see langword="null" /> where there are none or it permits nothing.</summary>
    /// <remarks>An account a reload has withdrawn permits nothing, the answer the action recorder gives for the same reading.</remarks>
    private AuthoredMailKeywords? KeywordsPermitted(
        MailAccountId account,
        MailboxMutation mutation,
        IEnumerable<string> keywords)
    {
        string[] owed = [.. keywords];

        if (owed.Length == 0)
        {
            return null;
        }

        try
        {
            return this.permissions.GetRuleActionPermissions(account).Permits(mutation)
                ? AuthoredMailKeywords.Create(owed)
                : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}

/// <summary>A spam mark and the junk keywords it still owes one message, worked out before the transaction it is written in.</summary>
/// <param name="Mark">What the person says the message is.</param>
/// <param name="Additions">The keywords it adds, or <see langword="null" /> where none is owed or the account permits none.</param>
/// <param name="Removals">The keywords it takes off, or <see langword="null" /> where none is owed or the account permits none.</param>
public sealed record PreparedSpamMark(SpamMark Mark, AuthoredMailKeywords? Additions, AuthoredMailKeywords? Removals);

/// <summary>What writing a spam mark staged in its transaction.</summary>
/// <param name="Records">The records its keyword changes opened, which a convergence pass carries to the mail server.</param>
/// <param name="Applied">The keyword changes committed to stored state, to announce once the transaction commits.</param>
/// <param name="DiscardedPassageCount">How many passages a spam mark removed.</param>
public sealed record StagedSpamMark(
    IReadOnlyList<MailboxMutationRecord> Records,
    IReadOnlyList<AppliedMailboxChange> Applied,
    int DiscardedPassageCount);
