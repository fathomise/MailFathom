// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Persistence;
using MailFathom.Application.Rules.Actions;
using MailFathom.Application.Spam.Signals;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Spam;

namespace MailFathom.Application.Spam.Actions;

/// <summary>Writes down the changes an operator asked a spam verdict to produce, as ordinary mutation records.</summary>
/// <remarks>
/// <para>
/// This is the whole join between a classification and a mailbox. Nothing here issues an IMAP command, opens a write
/// session, or touches the local row: it opens a durable record per change, and the account's own convergence pass
/// carries each one exactly as it carries a change somebody authored by hand. The local folder and flags change later,
/// because synchronization observed the server — never because this decided they should. A held account has no server
/// to observe, so there the flag, the keywords, and the filing are committed to the stored row instead, and no record is
/// opened. An account being restored to its source does both: it commits the same way and, where the message still
/// stands on the source, opens the records the convergence pass carries there, which the answer names beside the change.
/// </para>
/// <para>
/// Three switches decide what is asked for: filing into the junk folder, setting <c>\Seen</c>, and giving the message the
/// junk keywords RFC 9051 defines, which is how every other mail client sees the verdict. A spam verdict adds
/// <c>$Junk</c>, and <c>$Phishing</c> beside it for the phishing subcategory; <c>$Phishing</c> is never written without
/// <c>$Junk</c>, because a client warns only where both are set, and no verdict ever writes <c>$NotJunk</c>, which only
/// the person can say. The one keyword removal is the one RFC 9051 asks of a client, made on any verdict once a message
/// carries <c>$Junk</c> and <c>$NotJunk</c> together, a message moved back out of junk included: a pair somebody else
/// set is cleared whole, and a <c>$Junk</c> this deployment wrote is taken off alone, because the <c>$NotJunk</c>
/// beside it is the person correcting it. Only a filing withheld until its destination or its account is known again
/// withholds the removal with it, because that intent is performed whole once it is.
/// </para>
/// <para>
/// Two rules keep the switches from turning into an argument with the mailbox's user. An act already taken is not taken
/// again: a message filed once that is not in the destination is left alone but for that removal, because somebody
/// moved it back, and a keyword a verdict added once is not added again, because somebody took it off. Both are read
/// from the record
/// of what this deployment's verdicts did, which is written beside the act on every account whatever its custody phase,
/// so a held message moved back out of the local junk folder is left alone exactly as a mirrored one is. The filing
/// rule also reads the mutation records, which is where a mirrored filing older than that record is found.
/// </para>
/// <para>
/// The caller's posture decides whether the last step happens at all. A dry run takes every decision above and opens no
/// record, which is what lets a run over a whole mailbox report what it would do to somebody's mail before any of it
/// reaches their server. Nothing else about the work differs, so the two postures cannot disagree about one message.
/// </para>
/// <para>
/// Every change is written down in one commit, in the order it has to be applied: <c>\Seen</c>, then the keywords, then
/// the filing. On a server without <c>MOVE</c> a relocation gives the message a new UID, and a flag stored afterwards
/// would be aimed at an occurrence the source folder no longer holds. Each change is its own record, so a folder that
/// will not keep the keyword refuses that record alone with <c>MailboxMutationUnsupported</c>, and the others go ahead.
/// </para>
/// <para>
/// Where the junk folder is is not decided here. A verdict is one author of a filing mutation among others, so the
/// destination is turned into a folder by <see cref="MailboxDestinationResolver" />, which is the single place any
/// author reaches one — including the on-demand resolution a junk folder nothing mirrors needs, that being the
/// destination this feature recommends. Resolving happens before the commit is opened, because it can reach the mail
/// server.
/// </para>
/// <para>
/// Nothing it reads or reports is mail content. The occurrence, the folder alias, the keywords, the outcome, and the
/// record identifiers are safe to report; there is no path from here to a subject, an address, or a body.
/// </para>
/// </remarks>
public sealed class SpamActionRecorder
{
    private readonly ISpamActionSettingsReader settingsReader;
    private readonly ISpamActionOccurrenceReader occurrences;
    private readonly IMailboxMutationRecordStore records;
    private readonly ISpamVerdictActStore verdictActs;
    private readonly MailboxChangeSubmission submission;
    private readonly MailboxDestinationResolver destinations;
    private readonly IAuthoredDeleteEmailDispositionReader deleteDispositions;
    private readonly IMailRuleActionPermissionReader permissions;
    private readonly OptimisticConcurrencyRetryPolicy retryPolicy;

    /// <summary>Initializes the use case from the decisions it has to read and the submission it writes through.</summary>
    /// <param name="settingsReader">Answers what the message's account asked to happen to the junk in it.</param>
    /// <param name="occurrences">Reads where the classified email is, whether it is already read, and which keywords it carries.</param>
    /// <param name="records">Answers whether this feature asked for a message to be filed before it recorded its own acts.</param>
    /// <param name="verdictActs">Says what this deployment's verdicts already did to the message, and records what this one does.</param>
    /// <param name="submission">Records each change, or commits it where the account is held.</param>
    /// <param name="destinations">Turns the configured junk folder into the folder on the server it currently names.</param>
    /// <param name="deleteDispositions">Answers what the account keeps locally of mail that leaves the mirror for good.</param>
    /// <param name="permissions">Answers whether the account permits keywords to be written, which governs a verdict's as it governs a rule's.</param>
    /// <param name="retryPolicy">Commits the changes from a fresh read when a concurrent write conflicts.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public SpamActionRecorder(
        ISpamActionSettingsReader settingsReader,
        ISpamActionOccurrenceReader occurrences,
        IMailboxMutationRecordStore records,
        ISpamVerdictActStore verdictActs,
        MailboxChangeSubmission submission,
        MailboxDestinationResolver destinations,
        IAuthoredDeleteEmailDispositionReader deleteDispositions,
        IMailRuleActionPermissionReader permissions,
        OptimisticConcurrencyRetryPolicy retryPolicy)
    {
        ArgumentNullException.ThrowIfNull(settingsReader);
        ArgumentNullException.ThrowIfNull(occurrences);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(verdictActs);
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(destinations);
        ArgumentNullException.ThrowIfNull(deleteDispositions);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(retryPolicy);

        this.settingsReader = settingsReader;
        this.occurrences = occurrences;
        this.records = records;
        this.verdictActs = verdictActs;
        this.submission = submission;
        this.destinations = destinations;
        this.deleteDispositions = deleteDispositions;
        this.permissions = permissions;
        this.retryPolicy = retryPolicy;
    }

    /// <summary>Asks for whatever the account's switches say should happen to one classified message.</summary>
    /// <param name="classification">What classification concluded about the occurrence.</param>
    /// <param name="posture">Whether the changes are written down or only worked out.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The changes that were written down, what a dry run would have written down, or the reason there were none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="classification" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="posture" /> is not a defined member.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when every allowed commit attempt conflicted.</exception>
    /// <remarks>
    /// The checks run in the order of what they cost and of what they settle. The verdict is already in hand and
    /// answers first, because it is free and refuses most messages; the occurrence is then read, because the switches
    /// are the account's and the occurrence is what names it; the mailbox follows. A message nothing
    /// concluded junk about therefore costs no read at all, and one that is junk in a mailbox that asked for no action
    /// costs the occurrence row and nothing else.
    /// <para>
    /// The posture is read last of all, after every one of those checks. That is what makes a dry run a rehearsal rather
    /// than a prediction: a message a filing would be refused for reports the refusal in both postures, so an operator
    /// reading a dry run is reading the answers the acting run would reach and not a shorter list of them.
    /// </para>
    /// </remarks>
    public async Task<SpamActionResult> RecordAsync(
        SpamClassification classification,
        SpamActionPosture posture,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(classification);

        if (!Enum.IsDefined(posture))
        {
            throw new ArgumentOutOfRangeException(
                nameof(posture),
                posture,
                "An attempt either writes the changes down or works them out and writes nothing.");
        }

        var isSpam = classification.Verdict is SpamVerdict.Spam;

        // A verdict that is not spam asks the mailbox for something only where the person's $NotJunk may stand beside a
        // $Junk, and its signals already say whether a $NotJunk is there — so every other one ends before anything is read.
        if (!isSpam && !JunkKeywords.NameNotJunk(classification.Signals))
        {
            return SpamActionResult.NotActedOn(SpamActionOutcome.NotSpam);
        }

        var occurrence = await this.occurrences.FindAsync(classification.EmailId, cancellationToken);

        if (occurrence is null)
        {
            return SpamActionResult.NotActedOn(SpamActionOutcome.OccurrenceMissing);
        }

        var settings = this.settingsReader.ActionsFor(occurrence.Account);

        if (!settings.IsAnyActionEnabled)
        {
            return SpamActionResult.NotActedOn(SpamActionOutcome.NoActionConfigured);
        }

        var ownActs = await this.verdictActs.FindAsync(occurrence.Id, cancellationToken);
        var decision = isSpam
            ? await this.DecideSpamActionsAsync(classification, occurrence, settings, ownActs, cancellationToken)
            : this.DecideCorrectedMarks(occurrence, settings, ownActs);

        if (decision.Refusal is { } refusal)
        {
            return SpamActionResult.NotActedOn(refusal);
        }

        var plan = decision.Plan!;

        if (plan.IsEmpty)
        {
            return SpamActionResult.NotActedOn(SpamActionOutcome.NothingToChange);
        }

        if (posture is SpamActionPosture.DryRun)
        {
            return SpamActionResult.NotActedOn(SpamActionOutcome.WouldRequest);
        }

        return await this.OpenRecordsAsync(occurrence, classification.DecidedBy, plan, cancellationToken);
    }

    /// <summary>Decides what a spam verdict asks of the mailbox, or why it asks nothing.</summary>
    private async Task<ActionDecision> DecideSpamActionsAsync(
        SpamClassification classification,
        SpamActionOccurrence occurrence,
        SpamActionSettings settings,
        IReadOnlySet<SpamVerdictAct> ownActs,
        CancellationToken cancellationToken)
    {
        var removal = this.JunkMarksCleared(occurrence, settings, ownActs);
        var filing = await this.DecideFilingAsync(occurrence, settings, ownActs, cancellationToken);

        if (filing.Refusal is { } refusal)
        {
            // A message somebody moved back out of junk is left alone, but a pair standing on it is the RFC's to clear,
            // and that removal argues with nobody.
            return refusal is SpamActionOutcome.PreviouslyFiled && removal is not null
                ? ActionDecision.Planned(new ActionPlan(MarksRead: false, removal, Filing: null))
                : ActionDecision.Refused(refusal);
        }

        var marksRead = settings.MarksJunkRead && !occurrence.IsRemotelySeen;

        // A pair standing together is cleared in place of an addition: $Junk is already there, and RFC 9051 reads the
        // message as carrying neither until somebody takes them off.
        var marks = removal
            ?? (this.MayWriteJunkKeywords(occurrence.Account, settings, MailboxMutation.AddKeywords)
                ? JunkMarksOwed(classification, occurrence, ownActs)
                : null);

        return ActionDecision.Planned(new ActionPlan(marksRead, marks, filing.Plan));
    }

    /// <summary>Decides what a verdict resting on the person's <c>$NotJunk</c> asks of the mailbox, which is at most a removal.</summary>
    /// <remarks>
    /// An account that did not turn the keyword switch on, or a message carrying no <c>$Junk</c> beside the
    /// <c>$NotJunk</c>, owes nothing, and the verdict is reported as not spam rather than as an action it never asked for.
    /// </remarks>
    private ActionDecision DecideCorrectedMarks(
        SpamActionOccurrence occurrence,
        SpamActionSettings settings,
        IReadOnlySet<SpamVerdictAct> ownActs) =>
        this.JunkMarksCleared(occurrence, settings, ownActs) is { } removal
            ? ActionDecision.Planned(new ActionPlan(MarksRead: false, removal, Filing: null))
            : ActionDecision.Refused(SpamActionOutcome.NotSpam);

    /// <summary>Works out which junk keywords standing beside a <c>$NotJunk</c> are taken off, or <see langword="null" /> where none are.</summary>
    /// <remarks>
    /// <para>
    /// RFC 9051 has a client treat <c>$Junk</c> and <c>$NotJunk</c> together as neither and take them off. A pair somebody
    /// else set is cleared whole. A <c>$Junk</c> this deployment wrote is taken off alone, with a <c>$Phishing</c> it wrote
    /// beside it, because the <c>$NotJunk</c> is the person correcting that verdict and is what has to stand.
    /// </para>
    /// <para>
    /// It is asked for once per message, read from the record of what verdicts did, so a pair somebody sets again after
    /// it was cleared is left to them on a mirrored, a held, and a restoring account alike.
    /// </para>
    /// </remarks>
    private KeywordChange? JunkMarksCleared(
        SpamActionOccurrence occurrence,
        SpamActionSettings settings,
        IReadOnlySet<SpamVerdictAct> ownActs)
    {
        if (!JunkKeywords.Carries(occurrence.Keywords, JunkKeywords.Junk)
            || !JunkKeywords.Carries(occurrence.Keywords, JunkKeywords.NotJunk)
            || ownActs.Contains(SpamVerdictAct.ClearedJunkMarks)
            || !this.MayWriteJunkKeywords(occurrence.Account, settings, MailboxMutation.RemoveKeywords))
        {
            return null;
        }

        if (!ownActs.Contains(SpamVerdictAct.MarkedJunk))
        {
            return new KeywordChange(
                MailboxMutation.RemoveKeywords,
                AuthoredMailKeywords.Create([JunkKeywords.Junk, JunkKeywords.NotJunk]));
        }

        var takesOffPhishing = ownActs.Contains(SpamVerdictAct.MarkedPhishing)
            && JunkKeywords.Carries(occurrence.Keywords, JunkKeywords.Phishing);

        return new KeywordChange(
            MailboxMutation.RemoveKeywords,
            AuthoredMailKeywords.Create(takesOffPhishing
                ? [JunkKeywords.Junk, JunkKeywords.Phishing]
                : [JunkKeywords.Junk]));
    }

    /// <summary>Works out which junk keywords a spam verdict still owes the message, or <see langword="null" /> where it owes none.</summary>
    /// <remarks>
    /// A keyword the message already carries is not written, and neither is one a verdict of this deployment wrote once:
    /// a message that no longer carries it had it taken off, and writing it again would undo that. <c>$Phishing</c> is
    /// written only where <c>$Junk</c> will stand beside it, which is how a message whose <c>$Junk</c> somebody removed is
    /// left without either.
    /// </remarks>
    private static KeywordChange? JunkMarksOwed(
        SpamClassification classification,
        SpamActionOccurrence occurrence,
        IReadOnlySet<SpamVerdictAct> ownActs)
    {
        var carriesJunk = JunkKeywords.Carries(occurrence.Keywords, JunkKeywords.Junk);
        var addsJunk = !carriesJunk && !ownActs.Contains(SpamVerdictAct.MarkedJunk);
        var addsPhishing = classification.Subcategory is SpamSubcategory.Phishing
            && (carriesJunk || addsJunk)
            && !JunkKeywords.Carries(occurrence.Keywords, JunkKeywords.Phishing)
            && !ownActs.Contains(SpamVerdictAct.MarkedPhishing);

        var owed = new List<string>(capacity: 2);

        if (addsJunk)
        {
            owed.Add(JunkKeywords.Junk);
        }

        if (addsPhishing)
        {
            owed.Add(JunkKeywords.Phishing);
        }

        return owed.Count == 0
            ? null
            : new KeywordChange(MailboxMutation.AddKeywords, AuthoredMailKeywords.Create(owed));
    }

    /// <summary>Answers whether the account asks for junk keywords and permits keywords to be written at all.</summary>
    /// <remarks>
    /// The permission is the account's <c>WriteKeywords</c>, read per verdict for the reason a rule reads it per pass: the
    /// two sections reload independently, so a revocation has to reach the next verdict. An account a reload has
    /// withdrawn permits nothing.
    /// </remarks>
    private bool MayWriteJunkKeywords(MailAccountId account, SpamActionSettings settings, MailboxMutation mutation)
    {
        if (!settings.MarksJunkWithKeywords)
        {
            return false;
        }

        try
        {
            return this.permissions.GetRuleActionPermissions(account).Permits(mutation);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Decides whether a filing is owed, already satisfied, refused, or switched off.</summary>
    private async Task<FilingDecision> DecideFilingAsync(
        SpamActionOccurrence occurrence,
        SpamActionSettings settings,
        IReadOnlySet<SpamVerdictAct> ownActs,
        CancellationToken cancellationToken)
    {
        if (!settings.FilesJunk)
        {
            return FilingDecision.NothingOwed;
        }

        var resolved = await this.destinations.ResolveAsync(
            occurrence.Account,
            [settings.JunkFolder],
            cancellationToken);

        if (resolved.Find(settings.JunkFolder).Destination is not { } destination)
        {
            return FilingDecision.Refused(SpamActionOutcome.DestinationUnresolved);
        }

        if (destination.Alias == occurrence.FolderAlias)
        {
            return FilingDecision.NothingOwed;
        }

        // Asked after the folder is known and before anything is written, because it is the one answer that outranks
        // every switch but clearing a junk keyword pair: this feature has already asked for this email to be filed and the email is not in the
        // destination, so either somebody put it back or the change is still in flight.
        if (ownActs.Contains(SpamVerdictAct.FiledIntoJunk)
            || await this.records.HasRecordAsync(
                occurrence.Id,
                MailboxMutation.Relocate,
                MailboxMutationOrigin.Classification,
                cancellationToken))
        {
            return FilingDecision.Refused(SpamActionOutcome.PreviouslyFiled);
        }

        return this.PlanFiling(occurrence.Account, destination);
    }

    /// <summary>Builds the filing, carrying what becomes of the local copy exactly when the destination is unmirrored.</summary>
    /// <remarks>
    /// A junk folder MailFathom does not mirror is the recommended destination, so an unmirrored one is the ordinary case
    /// rather than the exception: the message leaves the mirror for good, and the account's own answer about mail it
    /// deletes is what decides whether the local copy is kept, tombstoned, or erased. An account a reload has stopped
    /// declaring has no such answer, and none invented here would be it, so the message is left alone until it is
    /// declared again — the same choice the rule path makes, reported rather than raised so a run over one mailbox is not
    /// ended by it.
    /// </remarks>
    private FilingDecision PlanFiling(MailAccountId accountId, MailboxDestination destination)
    {
        if (destination.IsMirrored)
        {
            return FilingDecision.Owed(new FilingPlan(destination, LocalDisposition: null));
        }

        try
        {
            return FilingDecision.Owed(new FilingPlan(
                destination,
                this.deleteDispositions.GetAuthoredDeleteDisposition(accountId)));
        }
        catch (InvalidOperationException)
        {
            return FilingDecision.Refused(SpamActionOutcome.AccountNoLongerConfigured);
        }
    }

    /// <summary>Opens the records, in the order the changes have to be applied, inside one commit.</summary>
    /// <remarks>
    /// On a held account the same submissions commit the changes instead, and clients are told only once the commit is
    /// durable. A restoring account commits in the same way and still opens the records its source is owed, so the
    /// answer is applied and carries them both. What the verdict did is recorded in the same commit as the change that
    /// did it, so the record can never claim an act whose change was rolled back.
    /// </remarks>
    private async Task<SpamActionResult> OpenRecordsAsync(
        SpamActionOccurrence occurrence,
        SpamClassificationStage decidedBy,
        ActionPlan plan,
        CancellationToken cancellationToken)
    {
        var applied = new List<AppliedMailboxChange>();
        SpamActionResult result;

        try
        {
            result = await this.retryPolicy.CommitAsync(
                async (session, attemptCancellationToken) =>
                {
                    applied.Clear();

                    var markedReadRecordId = plan.MarksRead
                        ? await this.MarkReadAsync(session, occurrence, decidedBy, applied, attemptCancellationToken)
                        : null;
                    var keywordsRecordId = plan.Keywords is { } change
                        ? await this.ChangeKeywordsAsync(session, occurrence, decidedBy, change, applied, attemptCancellationToken)
                        : null;
                    var filedRecordId = plan.Filing is { } filing
                        ? await this.FileAsync(session, occurrence, decidedBy, filing, applied, attemptCancellationToken)
                        : null;

                    // An account that committed locally answers as applied whatever else it wrote, because that is what
                    // happened to the mail; a restoring one carries its records inside that answer rather than instead
                    // of it.
                    if (applied.Count > 0)
                    {
                        return SpamActionResult.Applied(markedReadRecordId, filedRecordId, keywordsRecordId);
                    }

                    if (markedReadRecordId is not null || filedRecordId is not null || keywordsRecordId is not null)
                    {
                        return SpamActionResult.Requested(markedReadRecordId, filedRecordId, keywordsRecordId);
                    }

                    return SpamActionResult.NotActedOn(SpamActionOutcome.NothingToChange);
                },
                cancellationToken);
        }
        catch (RefusedSubmissionException refusal)
        {
            return SpamActionResult.NotActedOn(refusal.Outcome);
        }

        foreach (var change in applied)
        {
            this.submission.Announce(change);
        }

        return result;
    }

    private async Task<MailboxMutationRecordId?> MarkReadAsync(
        IPersistenceSession session,
        SpamActionOccurrence occurrence,
        SpamClassificationStage decidedBy,
        List<AppliedMailboxChange> applied,
        CancellationToken cancellationToken)
    {
        var seen = await this.submission.SubmitAsync(
            session,
            MailboxMutationRequest.SetSeen(
                occurrence.Id,
                occurrence.Occurrence,
                MailboxMutationRequester.Classification(decidedBy),
                isSeen: true),
            destination: null,
            heldUntil: null,
            cancellationToken);

        return RecordIdOf(seen, applied);
    }

    private async Task<MailboxMutationRecordId?> ChangeKeywordsAsync(
        IPersistenceSession session,
        SpamActionOccurrence occurrence,
        SpamClassificationStage decidedBy,
        KeywordChange change,
        List<AppliedMailboxChange> applied,
        CancellationToken cancellationToken)
    {
        var requester = MailboxMutationRequester.Classification(decidedBy, change.Keywords);
        var request = change.Mutation == MailboxMutation.AddKeywords
            ? MailboxMutationRequest.AddKeywords(occurrence.Id, occurrence.Occurrence, requester, change.Keywords)
            : MailboxMutationRequest.RemoveKeywords(occurrence.Id, occurrence.Occurrence, requester, change.Keywords);
        var submitted = await this.submission.SubmitAsync(
            session,
            request,
            destination: null,
            heldUntil: null,
            cancellationToken);
        var recordId = RecordIdOf(submitted, applied);

        if (Wrote(submitted))
        {
            foreach (var act in change.ActsRecorded)
            {
                await this.verdictActs.RecordAsync(session, occurrence.Id, act, cancellationToken);
            }
        }

        return recordId;
    }

    private async Task<MailboxMutationRecordId?> FileAsync(
        IPersistenceSession session,
        SpamActionOccurrence occurrence,
        SpamClassificationStage decidedBy,
        FilingPlan filing,
        List<AppliedMailboxChange> applied,
        CancellationToken cancellationToken)
    {
        var filed = await this.submission.SubmitAsync(
            session,
            MailboxMutationRequest.Relocate(
                occurrence.Id,
                occurrence.Occurrence,
                MailboxMutationRequester.Classification(decidedBy),
                filing.Destination.Path,
                filing.LocalDisposition),
            filing.Destination,
            heldUntil: null,
            cancellationToken);
        var recordId = RecordIdOf(filed, applied);

        if (Wrote(filed))
        {
            await this.verdictActs.RecordAsync(session, occurrence.Id, SpamVerdictAct.FiledIntoJunk, cancellationToken);
        }

        return recordId;
    }

    private static bool Wrote(SubmittedMailboxChange submitted) =>
        submitted.Outcome is MailboxChangeSubmissionOutcome.Recorded or MailboxChangeSubmissionOutcome.Applied;

    /// <summary>Reads the record one submission opened, keeping what it committed instead for the announcement.</summary>
    /// <exception cref="RefusedSubmissionException">Thrown when a held account refused the change, so the transaction carrying the others never commits.</exception>
    private static MailboxMutationRecordId? RecordIdOf(SubmittedMailboxChange submitted, List<AppliedMailboxChange> applied)
    {
        // Thrown rather than returned, because returning commits: a read change committed beside a filing that went
        // nowhere marks spam read while it stays in the inbox, which is what DestinationUnresolved exists to withhold.
        SpamActionOutcome? refusal = submitted.Outcome switch
        {
            MailboxChangeSubmissionOutcome.MessageMissing => SpamActionOutcome.OccurrenceMissing,
            MailboxChangeSubmissionOutcome.DestinationMissing => SpamActionOutcome.DestinationUnresolved,

            // SourceContentMissing is a copy's alone, and a verdict asks for a read, a keyword change, and a move, so no
            // outcome here is one this recorder can receive.
            _ => null,
        };

        if (refusal is { } outcome)
        {
            throw new RefusedSubmissionException(outcome);
        }

        if (submitted.Change is { } change)
        {
            applied.Add(change);
        }

        return submitted.Record?.Id;
    }

    /// <summary>Ends a commit whose held account refused one of its changes, carrying what the verdict ends as.</summary>
    [SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "A control-flow signal from the commit body to the method around it that never leaves this type.")]
    [SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "The exception carries the outcome and is never constructed from a message or an inner exception.")]
    [SuppressMessage("Usage", "RCS1194:Implement exception constructors", Justification = "The exception carries the outcome and is never constructed from a message or an inner exception.")]
    private sealed class RefusedSubmissionException(SpamActionOutcome outcome)
        : Exception($"A held account refused a spam action's change, ending it as {outcome}.")
    {
        public SpamActionOutcome Outcome { get; } = outcome;
    }

    /// <summary>Where a filing would put the message, and what the account keeps of it locally afterwards.</summary>
    private sealed record FilingPlan(MailboxDestination Destination, AuthoredDeleteEmailDisposition? LocalDisposition);

    /// <summary>A junk keyword change: the keywords a spam verdict adds, or the ones standing beside a <c>$NotJunk</c> that are taken off.</summary>
    private sealed record KeywordChange(MailboxMutation Mutation, AuthoredMailKeywords Keywords)
    {
        /// <summary>Gets what the change is recorded as once written: one act per keyword added, and one for a removal.</summary>
        internal IEnumerable<SpamVerdictAct> ActsRecorded => this.Mutation == MailboxMutation.RemoveKeywords
            ? [SpamVerdictAct.ClearedJunkMarks]
            : this.Keywords.Values.Select(static keyword => keyword switch
            {
                JunkKeywords.Phishing => SpamVerdictAct.MarkedPhishing,
                _ => SpamVerdictAct.MarkedJunk,
            });
    }

    /// <summary>Everything one verdict asks of the mailbox, in the order it is applied.</summary>
    private sealed record ActionPlan(bool MarksRead, KeywordChange? Keywords, FilingPlan? Filing)
    {
        internal bool IsEmpty => !this.MarksRead && this.Keywords is null && this.Filing is null;
    }

    /// <summary>What was decided about a verdict: a plan to carry out, or a reason to leave the message alone.</summary>
    private readonly record struct ActionDecision(ActionPlan? Plan, SpamActionOutcome? Refusal)
    {
        internal static ActionDecision Planned(ActionPlan plan) => new(plan, Refusal: null);

        internal static ActionDecision Refused(SpamActionOutcome outcome) => new(Plan: null, outcome);
    }

    /// <summary>What was decided about filing: a plan to carry out, nothing to do, or a reason to leave the message alone.</summary>
    /// <remarks>
    /// A refusal and an absent plan are held apart deliberately. Both end with no relocation, and only one of them also
    /// withholds the <c>\Seen</c> and keyword changes — which is the difference between a message that is already filed
    /// and a message whose filing has nowhere to go.
    /// </remarks>
    private readonly record struct FilingDecision(FilingPlan? Plan, SpamActionOutcome? Refusal)
    {
        /// <summary>Gets the decision that owes no relocation and withholds nothing — filing is off, or already done.</summary>
        internal static FilingDecision NothingOwed => default;

        internal static FilingDecision Owed(FilingPlan plan) => new(plan, Refusal: null);

        internal static FilingDecision Refused(SpamActionOutcome outcome) => new(Plan: null, outcome);
    }
}
