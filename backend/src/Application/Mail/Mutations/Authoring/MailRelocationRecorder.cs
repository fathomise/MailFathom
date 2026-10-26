// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Folders;
using MailFathom.Application.Folders.Local;
using MailFathom.Application.Mail.Mutations.Authoring.Failures;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam.Marking;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations.Authoring;

/// <summary>Writes down the folder moves a caller asks for, as the mutation records every requester uses.</summary>
/// <remarks>
/// <para>
/// It is the relocation half of what
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md">ADR 0007</see>
/// permits a caller to author, and it is built exactly as <see cref="MailFlagChangeRecorder" /> is and for the same
/// reasons: it asks for the grant before it asks about the email, it answers the same question about which mail may be
/// written as about which mail may be read, and it issues no IMAP command at all. The account's own convergence pass
/// carries the record to a completed or a dead-lettered ending, which is what keeps a move crash-safe — the sequence a
/// server without <c>MOVE</c> forces is a copy and a delete, and a record is what stops a crash between the two from
/// losing the message.
/// </para>
/// <para>
/// The grant is its own, and is not the one that writes flags. A flag misdescribes mail the user can still find; a
/// move puts the mail somewhere else, which is why <see cref="MailFathomPermission.MailMove" /> is a name a deployment
/// grants separately.
/// </para>
/// <para>
/// The destination is judged by the same visibility rule as the source. A folder an operator withheld from this caller
/// is no more a destination than it is a source, because filing mail into a folder the caller cannot read would be a
/// way of moving mail out of sight rather than a capability of its own.
/// </para>
/// <para>
/// The drafts folder and the sent folder are judged by what they hold as well as by who may see them. Each holds one kind
/// of mail — what the user is still writing, and what they have sent — together with every subfolder made beneath it, so
/// mail moves freely inside either tree, nothing is filed into either from outside it, and what is in either leaves it
/// only for the trash, which is how it is deleted. It is the rule a client offers its folders by, held here as well
/// because a client's offer is not a boundary. Where the two folders are is read from what the account's own
/// synchronization recorded rather than resolved on demand, so a move asks the mail server nothing about folders it
/// never named; and on a held account the question is the submission's, because the folder a message is in there is
/// local rather than the one it was stored under.
/// </para>
/// <para>
/// <b>The person moving a message into the account's junk folder is marking it spam.</b> The same transaction records
/// their spam verdict, with the subcategory they already named or none, and the junk keywords it carries, through
/// <see cref="SpamMarkWriter" /> and whatever the account's switch for filing junk says — they filed it themselves. A
/// move by any other reader is a move and nothing else, and a move out of the junk folder changes no verdict.
/// </para>
/// <para>
/// Every refusal is a result rather than an exception, because a caller moving several messages at once acts on each
/// answer and carries on with the rest. The one failure that is not about a message is: a caller without the grant
/// gets no answer about any of them, which is the whole request's outcome rather than one item's.
/// </para>
/// </remarks>
public sealed class MailRelocationRecorder
{
    private readonly AccessAuthorization authorization;
    private readonly MailboxScopeResolver scopeResolver;
    private readonly IAuthoredMailboxTargetReader targets;
    private readonly ILocalMailFolderStore folders;
    private readonly MailboxDestinationResolver destinations;
    private readonly IAuthoredDeleteEmailDispositionReader deleteDispositions;
    private readonly MailboxChangeSubmission submission;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly MailAccountRunSignal runSignal;
    private readonly IJunkMailFolderCatalog junkFolders;
    private readonly KeptMailTrees keptTrees;
    private readonly SpamMarkWriter spamMarks;

    /// <summary>Initializes the use case over the grant it asks first, the folder it files into, and the submission it writes through.</summary>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <param name="scopeResolver">Answers whether a caller may reach the folder an email is in and the folder it is going to.</param>
    /// <param name="targets">Answers where the named email currently is.</param>
    /// <param name="folderReferences">Answers which folder of the account plays the drafts role and which the sent role.</param>
    /// <param name="bindings">Answers where on the server those two folders were last found.</param>
    /// <param name="folders">Answers which folder of the account a folder identity names.</param>
    /// <param name="destinations">Turns the folder a caller named into the folder on the server it currently means.</param>
    /// <param name="deleteDispositions">Answers what the account keeps locally of mail that leaves the mirror for good.</param>
    /// <param name="submission">Records the move, or commits it where the account is held.</param>
    /// <param name="commitPolicy">Commits the change, retrying an optimistic conflict.</param>
    /// <param name="runSignal">Brings the account's next synchronization run forward, which is what carries the move to the mail server.</param>
    /// <param name="junkFolders">Answers whether the destination is the account's junk folder, which makes the person's move a spam mark.</param>
    /// <param name="spamMarks">Writes the person's spam verdict a move into the junk folder makes.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public MailRelocationRecorder(
        AccessAuthorization authorization,
        MailboxScopeResolver scopeResolver,
        IAuthoredMailboxTargetReader targets,
        MailFolderReferenceResolver folderReferences,
        IMailFolderResolutionStore bindings,
        ILocalMailFolderStore folders,
        MailboxDestinationResolver destinations,
        IAuthoredDeleteEmailDispositionReader deleteDispositions,
        MailboxChangeSubmission submission,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        MailAccountRunSignal runSignal,
        IJunkMailFolderCatalog junkFolders,
        SpamMarkWriter spamMarks)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(folderReferences);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(destinations);
        ArgumentNullException.ThrowIfNull(deleteDispositions);
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(runSignal);
        ArgumentNullException.ThrowIfNull(junkFolders);
        ArgumentNullException.ThrowIfNull(spamMarks);

        this.authorization = authorization;
        this.scopeResolver = scopeResolver;
        this.targets = targets;
        this.folders = folders;
        this.destinations = destinations;
        this.deleteDispositions = deleteDispositions;
        this.submission = submission;
        this.commitPolicy = commitPolicy;
        this.runSignal = runSignal;
        this.junkFolders = junkFolders;
        this.keptTrees = new KeptMailTrees(folderReferences, bindings);
        this.spamMarks = spamMarks;
    }

    /// <summary>Writes down one move, against the email and the folder a caller named.</summary>
    /// <param name="storedEmailId">The email to move, as a listing, a search, or a read returned it.</param>
    /// <param name="destination">The identity of the folder to move it into.</param>
    /// <param name="requester">The invocation asking, which is what decides whether asking again is the same request.</param>
    /// <param name="reader">Who the act is asked for: the person through the client, or an agent or an MCP caller.</param>
    /// <param name="cancellationToken">Cancels the resolution and the write.</param>
    /// <returns>The record that was opened, or the reason none was.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="requester" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the moving grant.</exception>
    /// <remarks>
    /// The destination is resolved before the commit is opened, because resolving a folder the account maps and does not
    /// mirror reaches the mail server, and a transaction must never be held open across one.
    /// </remarks>
    public async Task<AuthoredMailRelocationResult> RecordAsync(
        StoredEmailId storedEmailId,
        MailFolderId destination,
        MailboxMutationRequester requester,
        MailboxReaderKind reader,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requester);

        // The grant is asked for before the destination is read at all, so a caller that holds nothing is refused as
        // unauthorized rather than told whether the folder it named exists.
        this.authorization.RequirePermission(MailFathomPermission.MailMove);

        var target = await this.targets.FindAsync(storedEmailId, cancellationToken);

        // The two withheld folders are reported as the two absences they are. A source this caller may not read is a
        // message it may not learn about at all, and a destination it may not read is a folder it may not learn about,
        // so neither answer says anything about the other.
        var assignedAccounts = await this.scopeResolver.ReadAssignedAccountsAsync(cancellationToken);

        if (target is null || !this.scopeResolver.IsReadable(
                reader,
                assignedAccounts,
                target.Occurrence.AccountId,
                target.Folder.Alias))
        {
            return AuthoredMailRelocationResult.NotRecorded(MailRelocationOutcome.MessageNotFound);
        }

        var account = target.Occurrence.AccountId;
        var named = (await this.folders.ReadFoldersAsync(account, cancellationToken))
            .FirstOrDefault(folder => folder.Id == destination);

        if (named is null
            || ReferenceTo(named) is not { } reference
            || !this.scopeResolver.IsReadable(reader, assignedAccounts, account, named.SourceFolderAlias))
        {
            return AuthoredMailRelocationResult.NotRecorded(MailRelocationOutcome.DestinationNotFound);
        }

        var resolved = await this.destinations.ResolveAsync(account, [reference], cancellationToken);

        if (resolved.Find(reference).Destination is not { } folder)
        {
            return AuthoredMailRelocationResult.NotRecorded(MailRelocationOutcome.DestinationNotFound);
        }

        var crossesOnTheServer = await this.keptTrees.CrossesAsync(account, target.Folder, folder, cancellationToken);

        AuthoredDeleteEmailDisposition? localDisposition;

        try
        {
            localDisposition = folder.IsMirrored
                ? null
                : this.deleteDispositions.GetAuthoredDeleteDisposition(target.Occurrence.AccountId);
        }
        catch (InvalidOperationException)
        {
            return AuthoredMailRelocationResult.NotRecorded(MailRelocationOutcome.AccountNoLongerConfigured);
        }

        var request = MailboxMutationRequest.Relocate(
            storedEmailId,
            target.Occurrence,
            requester,
            folder.Path,
            localDisposition);

        var spamMark = reader is MailboxReaderKind.Person && this.IsJunkFolder(account, folder)
            ? await this.spamMarks.PrepareMoveIntoJunkAsync(storedEmailId, account, cancellationToken)
            : null;

        SubmittedMailboxChange submitted;
        StagedSpamMark? markedSpam = null;

        try
        {
            // Whether the message is already there is the submission's to answer, because it is a different question on a
            // held account: the alias a message was stored under is its source folder, and the folder it is in is local.
            submitted = await this.commitPolicy.CommitAsync(
                async (session, attemptCancellationToken) =>
                {
                    markedSpam = spamMark is null
                        ? null
                        : await this.spamMarks.StageAsync(
                            session,
                            storedEmailId,
                            target.Occurrence,
                            spamMark,
                            requester,
                            attemptCancellationToken);

                    var moved = await this.submission.SubmitMoveAsync(
                        session,
                        request,
                        folder,
                        crossesOnTheServer,
                        attemptCancellationToken);

                    if (markedSpam is not null)
                    {
                        await this.FinishSpamMarkAsync(session, storedEmailId, moved, attemptCancellationToken);
                    }

                    return moved;
                },
                cancellationToken);
        }
        catch (MoveNotMadeException refused)
        {
            submitted = refused.Submitted;
            markedSpam = null;
        }
        catch (AuthoredMailChangeTargetNotFoundException)
        {
            return AuthoredMailRelocationResult.NotRecorded(MailRelocationOutcome.MessageNotFound);
        }
        catch (MailFlagChangeInvalidException)
        {
            return AuthoredMailRelocationResult.NotRecorded(MailRelocationOutcome.RequestIdAlreadyAskedForAnother);
        }

        if (markedSpam is not null)
        {
            this.spamMarks.Announce(markedSpam);

            if (markedSpam.Records.Count > 0)
            {
                this.runSignal.BringForward(account);
            }
        }

        var moved = this.ResultOf(submitted, folder.Alias, account);

        return markedSpam is null ? moved : moved with { SpamMarkRecords = markedSpam.Records };
    }

    /// <summary>Answers what the submission made of the move, bringing the account's run forward for a record it opened.</summary>
    private AuthoredMailRelocationResult ResultOf(SubmittedMailboxChange submitted, MailFolderAlias destination, MailAccountId account)
    {
        switch (submitted.Outcome)
        {
            case MailboxChangeSubmissionOutcome.Recorded when submitted.Record is { } record:
                // Raised once the record is durable, for the reason MailFlagChangeRecorder gives: the run reads the
                // records rather than the raise.
                this.runSignal.BringForward(account);

                return AuthoredMailRelocationResult.Recorded(destination, record.Id, record.Lifecycle);
            case MailboxChangeSubmissionOutcome.Applied when submitted.Change is { } change:
                this.submission.Announce(change);

                // A restoring account's move is applied and recorded at once, so the record its source is still owed is
                // brought forward exactly as a mirrored account's is and reported beside the move that already happened.
                if (submitted.Record is { } carried)
                {
                    this.runSignal.BringForward(account);

                    return AuthoredMailRelocationResult.Applied(destination, carried.Id, carried.Lifecycle);
                }

                return AuthoredMailRelocationResult.Applied(destination);
            case MailboxChangeSubmissionOutcome.AlreadyInDestination:
                return AuthoredMailRelocationResult.NotRecorded(MailRelocationOutcome.AlreadyInDestination);
            case MailboxChangeSubmissionOutcome.DestinationMissing:
                return AuthoredMailRelocationResult.NotRecorded(MailRelocationOutcome.DestinationNotFound);
            case MailboxChangeSubmissionOutcome.DestinationNotAllowed:
                return AuthoredMailRelocationResult.NotRecorded(MailRelocationOutcome.DestinationNotAllowed);
            default:
                return AuthoredMailRelocationResult.NotRecorded(MailRelocationOutcome.MessageNotFound);
        }
    }

    /// <summary>Answers whether a destination is the account's junk folder, by the role it plays or the mapping that carries the role.</summary>
    private bool IsJunkFolder(MailAccountId account, MailboxDestination destination) =>
        destination.Role is MailFolderSpecialUse.Junk || this.junkFolders.IsJunkFolder(account, destination.Alias);

    /// <summary>Records the person's filing as a verdict's act, or takes the spam mark back with a move that was not made.</summary>
    /// <exception cref="MoveNotMadeException">Thrown when nothing moved, so the mark staged beside it never commits.</exception>
    /// <remarks>
    /// The mark is part of the move, so a move that was not made — refused, or into the folder the message is already in —
    /// leaves no verdict and no keyword change behind it, and its answer stays the one a move alone would give.
    /// </remarks>
    private async Task FinishSpamMarkAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        SubmittedMailboxChange moved,
        CancellationToken cancellationToken)
    {
        switch (moved.Outcome)
        {
            case MailboxChangeSubmissionOutcome.Recorded or MailboxChangeSubmissionOutcome.Applied:
                await this.spamMarks.RecordFiledIntoJunkAsync(session, storedEmailId, cancellationToken);
                break;
            default:
                throw new MoveNotMadeException(moved);
        }
    }

    /// <summary>Names a folder the way the destination resolver finds it, or nothing where it cannot be filed into yet.</summary>
    /// <remarks>
    /// A folder a source binding feeds is named by that binding's alias and a protected one by its role, which reaches
    /// the same folder on a held account and the folder on the server on a mirrored one.
    /// </remarks>
    // A folder somebody created on a held account carries neither, so it is not yet a destination; filing by
    // the folder itself arrives with the custodian that owns a held account's moves (#2579).
    private static MailFolderReference? ReferenceTo(LocalMailFolder folder) =>
        folder.SourceFolderAlias is { } alias ? MailFolderReference.ToAlias(alias)
        : folder.Role is { } role ? MailFolderReference.ToRole(role)
        : null;

    /// <summary>Ends a commit whose move into the junk folder was refused, so the spam mark staged before it is rolled back with it.</summary>
    [SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "A control-flow signal from the commit body to the method around it that never leaves this type.")]
    private sealed class MoveNotMadeException(SubmittedMailboxChange submitted)
        : Exception($"A move into the junk folder ended as {submitted.Outcome}, so the spam mark beside it was not committed.")
    {
        public SubmittedMailboxChange Submitted { get; } = submitted;
    }
}
