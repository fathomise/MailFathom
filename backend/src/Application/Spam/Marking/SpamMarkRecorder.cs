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
/// spam, read per message's account. With it on, spam is filed into the account's junk destination and not spam takes a
/// message sitting in the junk folder back to the inbox, each in the same transaction as the verdict; a message already
/// where the mark would put it stays there. With it off a mark changes the verdict and the keywords and moves nothing.
/// The client asks before an act that moves, so this takes the call as already confirmed. Marking read stays with
/// automated verdicts and is never part of the person's act.
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
/// its keywords are marks on mail; a mark that moves a message asks for <see cref="MailFathomPermission.MailMove" /> as
/// well, because the move puts mail somewhere else whichever act asked for it.
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
        MailAccountRunSignal runSignal)
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
    }

    /// <summary>Records the person's mark on one message, with the keywords and the move it carries.</summary>
    /// <param name="storedEmailId">The message, as a listing, a conversation, or a search published it.</param>
    /// <param name="mark">What the person says the message is.</param>
    /// <param name="requester">The invocation asking, which is what decides whether asking again is the same request.</param>
    /// <param name="cancellationToken">Cancels the resolution and the write.</param>
    /// <returns>What was recorded, or why nothing was.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="mark" /> or <paramref name="requester" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the grant the mark needs.</exception>
    /// <remarks>
    /// Repeating a mark is one change: the verdict is replaced by the same verdict, the keywords the message already
    /// carries are not asked for again, and a move into the folder the message is in is no move. Marking spam again with
    /// another subcategory replaces the person's subcategory. Every destination is resolved before the transaction opens,
    /// because resolving a folder the account does not mirror can reach the mail server.
    /// </remarks>
    public async Task<SpamMarkResult> RecordAsync(
        StoredEmailId storedEmailId,
        SpamMark mark,
        MailboxMutationRequester requester,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mark);
        ArgumentNullException.ThrowIfNull(requester);

        this.authorization.RequirePermission(MailFathomPermission.MailFlagsWrite);

        var target = await this.targets.FindAsync(storedEmailId, cancellationToken);

        if (target is null
            || !await this.scopeResolver.IsReadableAsync(
                MailboxReaderKind.Person,
                target.Occurrence.AccountId,
                target.Folder.Alias,
                cancellationToken))
        {
            return SpamMarkResult.NotRecorded(storedEmailId, SpamMarkOutcome.MessageNotFound);
        }

        var account = target.Occurrence.AccountId;
        var move = await this.PlanMoveAsync(target, mark, cancellationToken);

        if (move is not null)
        {
            this.authorization.RequirePermission(MailFathomPermission.MailMove);
        }

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
    /// resolves to nothing, or an unmirrored one the account no longer declares what to keep of, moves nothing, and the
    /// verdict is recorded all the same: the mark is the person's whether or not the mailbox can be reorganized for it.
    /// </remarks>
    private async Task<PlannedMove?> PlanMoveAsync(
        AuthoredMailboxTarget target,
        SpamMark mark,
        CancellationToken cancellationToken)
    {
        var account = target.Occurrence.AccountId;
        var settings = this.settingsReader.ActionsFor(account);

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

        if (resolved.Find(destinationReference).Destination is not { } destination)
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
