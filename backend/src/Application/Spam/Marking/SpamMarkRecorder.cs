// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Folders;
using MailFathom.Application.Folders.Local;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Authoring;
using MailFathom.Application.Mail.Mutations.Authoring.Failures;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam.Actions;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Spam;

namespace MailFathom.Application.Spam.Marking;

/// <summary>Records the person's own verdict that a message is spam, of a kind they may name, or not spam.</summary>
/// <remarks>
/// <para>
/// The person's verdict is final: it outranks every stage, the deterministic one included, and nothing replaces it but
/// the person. Not spam is a statement the person made rather than the absence of a spam verdict, and it is the one way a
/// <see cref="SpamVerdict.Spam" /> verdict is lifted — moving a message out of the junk folder never is.
/// </para>
/// <para>
/// <b>Whether a mark moves the message is the account's <c>MoveToJunkFolder</c> switch</b>, the one that files automated
/// spam, read once per account for a whole batch. With it on, spam is filed into the account's junk destination and not spam takes a
/// message sitting in the junk folder back to the inbox, each in the same transaction as the verdict; a message already
/// where the mark would put it stays there. With it off a mark changes the verdict and the keywords and moves nothing.
/// This asks for no confirmation of its own, so a call is taken as confirmed. Marking read stays with automated verdicts
/// and is never part of the person's act.
/// </para>
/// <para>
/// A filing this makes is recorded as an act of a verdict, which is the record an operator's switches read before they
/// file a message: so a message the person filed and somebody moved back out — in any client, or by a server filter — is
/// never filed again by a verdict. A message the person took out of junk is not filed back by any verdict either, because
/// no verdict replaces the person's not spam.
/// </para>
/// <para>
/// Each message is one transaction, so a batch over mail that changed since the screen drew it reports exactly which
/// messages did not take the mark. The grant is <see cref="MailFathomPermission.MailFlagsWrite" />, since a verdict and
/// its keywords are marks on mail; a mark on mail of an account whose switch files junk asks for
/// <see cref="MailFathomPermission.MailMove" /> as well, because the move puts mail somewhere else whichever act asked for
/// it. <see cref="RequireGrantsAsync" /> asks both for a whole batch before any message of it is written and returns the
/// switches it decided them from, which every mark of the batch then moves by, so a refusal is the request's and never
/// arrives after part of it committed. A mark's move passes the checks a person's own move
/// does: a destination the person may not read, or one across the edge of the drafts or the sent tree, is no move.
/// </para>
/// </remarks>
public sealed class SpamMarkRecorder
{
    private readonly AccessAuthorization authorization;
    private readonly MailboxScopeResolver scopeResolver;
    private readonly IAuthoredMailboxTargetReader targets;
    private readonly ISpamActionSettingsReader settingsReader;
    private readonly IJunkMailFolderCatalog junkFolders;
    private readonly ILocalMailFolderStore folders;
    private readonly MailboxDestinationResolver destinations;
    private readonly IAuthoredDeleteEmailDispositionReader deleteDispositions;
    private readonly SpamMarkWriter writer;
    private readonly MailboxChangeSubmission submission;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly MailAccountRunSignal runSignal;
    private readonly KeptMailTrees keptTrees;

    /// <summary>Initializes the use case over the grant it asks first, what decides a move, and the writer the verdict goes through.</summary>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <param name="scopeResolver">Answers whether the person may reach the folder a message is in.</param>
    /// <param name="targets">Answers where the named message currently is.</param>
    /// <param name="settingsReader">Answers whether the message's account files junk, and where.</param>
    /// <param name="junkFolders">Answers whether a folder is its account's junk folder.</param>
    /// <param name="folders">Answers which folder the message is in, which on a held account is a local one.</param>
    /// <param name="destinations">Turns the junk destination or the inbox into the folder on the server it currently names.</param>
    /// <param name="deleteDispositions">Answers what the account keeps locally of mail filed into a folder it does not mirror.</param>
    /// <param name="writer">Writes the verdict and the keywords that follow it.</param>
    /// <param name="submission">Records or commits the move through the account's custodian.</param>
    /// <param name="commitPolicy">Commits one message's mark, retrying an optimistic conflict.</param>
    /// <param name="runSignal">Brings the account's next synchronization run forward, which carries what was written down to the mail server.</param>
    /// <param name="folderReferences">Answers which folder of the account plays the drafts role and which the sent role.</param>
    /// <param name="bindings">Answers where on the server those two folders were last found.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public SpamMarkRecorder(
        AccessAuthorization authorization,
        MailboxScopeResolver scopeResolver,
        IAuthoredMailboxTargetReader targets,
        ISpamActionSettingsReader settingsReader,
        IJunkMailFolderCatalog junkFolders,
        ILocalMailFolderStore folders,
        MailboxDestinationResolver destinations,
        IAuthoredDeleteEmailDispositionReader deleteDispositions,
        SpamMarkWriter writer,
        MailboxChangeSubmission submission,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        MailAccountRunSignal runSignal,
        MailFolderReferenceResolver folderReferences,
        IMailFolderResolutionStore bindings)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(settingsReader);
        ArgumentNullException.ThrowIfNull(junkFolders);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(destinations);
        ArgumentNullException.ThrowIfNull(deleteDispositions);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(runSignal);
        ArgumentNullException.ThrowIfNull(folderReferences);
        ArgumentNullException.ThrowIfNull(bindings);

        this.authorization = authorization;
        this.scopeResolver = scopeResolver;
        this.targets = targets;
        this.settingsReader = settingsReader;
        this.junkFolders = junkFolders;
        this.folders = folders;
        this.destinations = destinations;
        this.deleteDispositions = deleteDispositions;
        this.writer = writer;
        this.submission = submission;
        this.commitPolicy = commitPolicy;
        this.runSignal = runSignal;
        this.keptTrees = new KeptMailTrees(folderReferences, bindings);
    }

    /// <summary>Asks for every grant a batch of marks needs, before any message of it is written.</summary>
    /// <param name="storedEmailIds">The messages the batch names.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The batch, carrying the switches of each account it reaches as they were read for the grants.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="storedEmailIds" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller lacks the flag grant, or the moving grant while a message of the batch is on an account whose switch files junk.</exception>
    /// <remarks>
    /// The moving grant follows the account's switch rather than whether a given message would move, so the answer
    /// costs no folder resolution and reads the same for a caller who holds the grant and one who does not.
    /// </remarks>
    public async Task<SpamMarkBatch> RequireGrantsAsync(IReadOnlyList<StoredEmailId> storedEmailIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storedEmailIds);

        this.authorization.RequirePermission(MailFathomPermission.MailFlagsWrite);

        var settingsByAccount = new Dictionary<MailAccountId, SpamActionSettings>();

        foreach (var storedEmailId in storedEmailIds)
        {
            if (await this.ReadableTargetAsync(storedEmailId, cancellationToken) is not { } target
                || settingsByAccount.ContainsKey(target.Occurrence.AccountId))
            {
                continue;
            }

            var settings = this.settingsReader.ActionsFor(target.Occurrence.AccountId);
            this.RequireMoveGrantWhereTheSwitchFilesJunk(settings);
            settingsByAccount[target.Occurrence.AccountId] = settings;
        }

        return new SpamMarkBatch(settingsByAccount);
    }

    /// <summary>Records the person's mark on one message, with the keywords and the move it carries.</summary>
    /// <param name="storedEmailId">The message, as a listing, a conversation, or a search published it.</param>
    /// <param name="mark">What the person says the message is.</param>
    /// <param name="requester">The invocation asking, which is what decides whether asking again is the same request.</param>
    /// <param name="batch">The batch the message belongs to, whose switches decide the move.</param>
    /// <param name="cancellationToken">Cancels the resolution and the write.</param>
    /// <returns>What was recorded, or why nothing was.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="mark" />, <paramref name="requester" />, or <paramref name="batch" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the grant the mark needs.</exception>
    /// <remarks>
    /// Repeating a mark is one change: the verdict is replaced by the same verdict, the keywords the message already
    /// carries are not asked for again, and a move into the folder the message is in is no move. Marking spam again with
    /// another subcategory replaces the person's subcategory. Every destination is resolved before the transaction opens,
    /// because resolving a folder the account does not mirror can reach the mail server. A message on an account the batch
    /// did not reach when it was admitted is reported as not found, since its grants were never decided.
    /// </remarks>
    public async Task<SpamMarkResult> RecordAsync(
        StoredEmailId storedEmailId,
        SpamMark mark,
        MailboxMutationRequester requester,
        SpamMarkBatch batch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mark);
        ArgumentNullException.ThrowIfNull(requester);
        ArgumentNullException.ThrowIfNull(batch);

        this.authorization.RequirePermission(MailFathomPermission.MailFlagsWrite);

        if (await this.ReadableTargetAsync(storedEmailId, cancellationToken) is not { } target
            || batch.SettingsFor(target.Occurrence.AccountId) is not { } settings)
        {
            return SpamMarkResult.NotRecorded(storedEmailId, SpamMarkOutcome.MessageNotFound);
        }

        var account = target.Occurrence.AccountId;
        this.RequireMoveGrantWhereTheSwitchFilesJunk(settings);
        var move = await this.PlanMoveAsync(target, mark, settings, cancellationToken);

        var prepared = await this.writer.PrepareAsync(storedEmailId, account, mark, cancellationToken);

        StagedSpamMark staged;
        SubmittedMailboxChange? moved = null;

        try
        {
            staged = await this.commitPolicy.CommitAsync(
                async (session, attemptCancellationToken) =>
                {
                    var written = await this.writer.StageAsync(
                        session,
                        storedEmailId,
                        target.Occurrence,
                        prepared,
                        requester,
                        attemptCancellationToken);

                    moved = move is null
                        ? null
                        : await this.MoveAsync(session, storedEmailId, target, mark, move, requester, attemptCancellationToken);

                    return written;
                },
                cancellationToken);
        }
        catch (AuthoredMailChangeTargetNotFoundException)
        {
            return SpamMarkResult.NotRecorded(storedEmailId, SpamMarkOutcome.MessageNotFound);
        }
        catch (MailFlagChangeInvalidException)
        {
            return SpamMarkResult.NotRecorded(storedEmailId, SpamMarkOutcome.RequestIdAlreadyAskedForAnother);
        }

        this.writer.Announce(staged);

        if (moved?.Change is { } movedLocally)
        {
            this.submission.Announce(movedLocally);
        }

        MailboxMutationRecord[] records = moved?.Record is { } moveRecord ? [.. staged.Records, moveRecord] : [.. staged.Records];

        if (records.Length > 0)
        {
            this.runSignal.BringForward(account);
        }

        var isMoved = moved?.Outcome is MailboxChangeSubmissionOutcome.Recorded or MailboxChangeSubmissionOutcome.Applied;
        var outcome = staged.Applied.Count > 0 || moved?.Change is not null || records.Length == 0
            ? SpamMarkOutcome.Applied
            : SpamMarkOutcome.Recorded;

        return new SpamMarkResult(storedEmailId, outcome, records, isMoved);
    }

    /// <summary>Decides where the mark moves the message, or that it moves it nowhere.</summary>
    /// <remarks>
    /// Spam is filed into the account's junk destination unless the message is already in the junk folder; not spam takes
    /// a message in the junk folder back to the inbox and leaves every other message where it is. A destination that
    /// resolves to nothing, one the person may not read, one across the edge of the drafts or the sent tree, and an
    /// unmirrored one the account no longer declares what to keep of each move nothing, exactly as the person's own move
    /// would be refused, and the verdict is recorded all the same: the mark is the person's whether or not the mailbox can
    /// be reorganized for it.
    /// </remarks>
    private async Task<PlannedMove?> PlanMoveAsync(
        AuthoredMailboxTarget target,
        SpamMark mark,
        SpamActionSettings settings,
        CancellationToken cancellationToken)
    {
        var account = target.Occurrence.AccountId;

        if (!settings.FilesJunk)
        {
            return null;
        }

        var isInJunk = await this.IsInJunkFolderAsync(target, cancellationToken);
        MailFolderReference? reference = mark.Verdict is SpamVerdict.Spam
            ? isInJunk ? null : settings.JunkFolder
            : isInJunk ? MailFolderReference.ToRole(MailFolderSpecialUse.Inbox) : null;

        if (reference is not { } destinationReference)
        {
            return null;
        }

        var resolved = await this.destinations.ResolveAsync(account, [destinationReference], cancellationToken);

        if (resolved.Find(destinationReference).Destination is not { } destination
            || !await this.scopeResolver.IsReadableAsync(MailboxReaderKind.Person, account, destination.Alias, cancellationToken)
            || await this.keptTrees.CrossesAsync(account, target.Folder, destination, cancellationToken))
        {
            return null;
        }

        if (destination.IsMirrored)
        {
            return new PlannedMove(destination, LocalDisposition: null);
        }

        try
        {
            return new PlannedMove(destination, this.deleteDispositions.GetAuthoredDeleteDisposition(account));
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Finds where the message is, or nothing where this deployment serves the person no readable message under that identity.</summary>
    private async Task<AuthoredMailboxTarget?> ReadableTargetAsync(StoredEmailId storedEmailId, CancellationToken cancellationToken) =>
        await this.targets.FindAsync(storedEmailId, cancellationToken) is { } target
        && await this.scopeResolver.IsReadableAsync(
            MailboxReaderKind.Person,
            target.Occurrence.AccountId,
            target.Folder.Alias,
            cancellationToken)
            ? target
            : null;

    /// <summary>Asks for the moving grant where the account's switches file junk.</summary>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the switches file junk and the caller cannot move mail.</exception>
    private void RequireMoveGrantWhereTheSwitchFilesJunk(SpamActionSettings settings)
    {
        if (settings.FilesJunk)
        {
            this.authorization.RequirePermission(MailFathomPermission.MailMove);
        }
    }

    /// <summary>Answers whether the message is in its account's junk folder, read from the folder it is in rather than the one it was stored under.</summary>
    /// <remarks>
    /// The two differ on a held account, whose mail is moved between local folders. A folder the account no longer lists
    /// falls back to the folder the message was stored under, which is the only other answer there is.
    /// </remarks>
    private async Task<bool> IsInJunkFolderAsync(AuthoredMailboxTarget target, CancellationToken cancellationToken)
    {
        var account = target.Occurrence.AccountId;
        var current = (await this.folders.ReadFoldersAsync(account, cancellationToken))
            .FirstOrDefault(folder => folder.Id == target.StoredIn);

        return current is null
            ? this.junkFolders.IsJunkFolder(account, target.Folder.Alias)
            : current.Role is MailFolderSpecialUse.Junk
                || (current.SourceFolderAlias is { } alias && this.junkFolders.IsJunkFolder(account, alias));
    }

    /// <summary>Records or commits the move a mark carries, after its keywords, and records a spam filing as a verdict's act.</summary>
    private async Task<SubmittedMailboxChange> MoveAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        AuthoredMailboxTarget target,
        SpamMark mark,
        PlannedMove move,
        MailboxMutationRequester requester,
        CancellationToken cancellationToken)
    {
        var submitted = await this.submission.SubmitMoveAsync(
            session,
            MailboxMutationRequest.Relocate(
                storedEmailId,
                target.Occurrence,
                requester,
                move.Destination.Path,
                move.LocalDisposition),
            move.Destination,
            crossesDraftsOrSentTreeOnTheServer: false,
            cancellationToken);

        if (submitted.Outcome is MailboxChangeSubmissionOutcome.MessageMissing)
        {
            throw new AuthoredMailChangeTargetNotFoundException();
        }

        if (mark.Verdict is SpamVerdict.Spam
            && submitted.Outcome is MailboxChangeSubmissionOutcome.Recorded or MailboxChangeSubmissionOutcome.Applied)
        {
            await this.writer.RecordFiledIntoJunkAsync(session, storedEmailId, cancellationToken);
        }

        return submitted;
    }

    /// <summary>Where a mark moves the message, and what the account keeps of it locally where the destination is unmirrored.</summary>
    private sealed record PlannedMove(MailboxDestination Destination, AuthoredDeleteEmailDisposition? LocalDisposition);
}
