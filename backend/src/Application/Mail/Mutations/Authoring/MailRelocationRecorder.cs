// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Persistence;
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
    private readonly MailFolderReferenceResolver folderReferences;
    private readonly IMailFolderResolutionStore bindings;
    private readonly MailboxDestinationResolver destinations;
    private readonly IAuthoredDeleteEmailDispositionReader deleteDispositions;
    private readonly MailboxChangeSubmission submission;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly MailAccountRunSignal runSignal;

    /// <summary>Initializes the use case over the grant it asks first, the folder it files into, and the submission it writes through.</summary>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <param name="scopeResolver">Answers whether a caller may reach the folder an email is in and the folder it is going to.</param>
    /// <param name="targets">Answers where the named email currently is.</param>
    /// <param name="folderReferences">Answers which folder of the account plays the drafts role and which the sent role.</param>
    /// <param name="bindings">Answers where on the server those two folders were last found.</param>
    /// <param name="destinations">Turns the folder a caller named into the folder on the server it currently means.</param>
    /// <param name="deleteDispositions">Answers what the account keeps locally of mail that leaves the mirror for good.</param>
    /// <param name="submission">Records the move, or commits it where the account is held.</param>
    /// <param name="commitPolicy">Commits the change, retrying an optimistic conflict.</param>
    /// <param name="runSignal">Brings the account's next synchronization run forward, which is what carries the move to the mail server.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public MailRelocationRecorder(
        AccessAuthorization authorization,
        MailboxScopeResolver scopeResolver,
        IAuthoredMailboxTargetReader targets,
        MailFolderReferenceResolver folderReferences,
        IMailFolderResolutionStore bindings,
        MailboxDestinationResolver destinations,
        IAuthoredDeleteEmailDispositionReader deleteDispositions,
        MailboxChangeSubmission submission,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        MailAccountRunSignal runSignal)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(folderReferences);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(destinations);
        ArgumentNullException.ThrowIfNull(deleteDispositions);
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(runSignal);

        this.authorization = authorization;
        this.scopeResolver = scopeResolver;
        this.targets = targets;
        this.folderReferences = folderReferences;
        this.bindings = bindings;
        this.destinations = destinations;
        this.deleteDispositions = deleteDispositions;
        this.submission = submission;
        this.commitPolicy = commitPolicy;
        this.runSignal = runSignal;
    }

    /// <summary>Writes down one move, against the email and the folder a caller named.</summary>
    /// <param name="storedEmailId">The email to move, as a listing, a search, or a read returned it.</param>
    /// <param name="destination">MailFathom's own name for the folder to move it into.</param>
    /// <param name="requester">The invocation asking, which is what decides whether asking again is the same request.</param>
    /// <param name="cancellationToken">Cancels the resolution and the write.</param>
    /// <returns>The record that was opened, or the reason none was.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="requester" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="destination" /> names no folder.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the moving grant.</exception>
    /// <remarks>
    /// The destination is resolved before the commit is opened, because resolving a folder the account maps and does not
    /// mirror reaches the mail server, and a transaction must never be held open across one.
    /// </remarks>
    public async Task<AuthoredMailRelocationResult> RecordAsync(
        StoredEmailId storedEmailId,
        MailFolderAlias destination,
        MailboxMutationRequester requester,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requester);

        // The grant is asked for before the destination is read at all, so a caller that holds nothing is refused as
        // unauthorized rather than told its argument was malformed. Today's one entry point filters an empty alias
        // before this is reached, which is exactly why the order has to be stated here rather than relied on there.
        this.authorization.RequirePermission(MailFathomPermission.MailMove);

        if (string.IsNullOrEmpty(destination.Value))
        {
            throw new ArgumentException("A move names the folder it files into.", nameof(destination));
        }

        var target = await this.targets.FindAsync(storedEmailId, cancellationToken);

        // The two withheld folders are reported as the two absences they are. A source this caller may not read is a
        // message it may not learn about at all, and a destination it may not read is a folder it may not learn about,
        // so neither answer says anything about the other.
        var assignedAccounts = await this.scopeResolver.ReadAssignedAccountsAsync(cancellationToken);

        if (target is null || !this.scopeResolver.IsReadableByTools(
                assignedAccounts,
                target.Occurrence.AccountId,
                target.Folder.Alias))
        {
            return AuthoredMailRelocationResult.NotRecorded(MailRelocationOutcome.MessageNotFound);
        }

        if (!this.scopeResolver.IsReadableByTools(
                assignedAccounts,
                target.Occurrence.AccountId,
                destination))
        {
            return AuthoredMailRelocationResult.NotRecorded(MailRelocationOutcome.DestinationNotFound);
        }

        var account = target.Occurrence.AccountId;
        var reference = MailFolderReference.ToAlias(destination);
        var resolved = await this.destinations.ResolveAsync(account, [reference], cancellationToken);

        if (resolved.Find(reference).Destination is not { } folder)
        {
            return AuthoredMailRelocationResult.NotRecorded(MailRelocationOutcome.DestinationNotFound);
        }

        var keptTrees = await this.ReadKeptTreeRootsAsync(account, cancellationToken);
        var crossesOnTheServer = CrossesKeptTree(target.Folder, folder, keptTrees);

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

        // Whether the message is already there is the submission's to answer, because it is a different question on a
        // held account: the alias a message was stored under is its source folder, and the folder it is in is local.
        var submitted = await this.commitPolicy.CommitAsync(
            (session, attemptCancellationToken) => this.submission.SubmitMoveAsync(
                session,
                request,
                folder,
                crossesOnTheServer,
                attemptCancellationToken),
            cancellationToken);

        switch (submitted.Outcome)
        {
            case MailboxChangeSubmissionOutcome.Recorded when submitted.Record is { } record:
                // Raised once the record is durable, for the reason MailFlagChangeRecorder gives: the run reads the
                // records rather than the raise.
                this.runSignal.BringForward(target.Occurrence.AccountId);

                return AuthoredMailRelocationResult.Recorded(folder.Alias, record.Id, record.Lifecycle);
            case MailboxChangeSubmissionOutcome.Applied when submitted.Change is { } change:
                this.submission.Announce(change);

                // A restoring account's move is applied and recorded at once, so the record its source is still owed is
                // brought forward exactly as a mirrored account's is and reported beside the move that already happened.
                if (submitted.Record is { } carried)
                {
                    this.runSignal.BringForward(target.Occurrence.AccountId);

                    return AuthoredMailRelocationResult.Applied(folder.Alias, carried.Id, carried.Lifecycle);
                }

                return AuthoredMailRelocationResult.Applied(folder.Alias);
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

    /// <summary>Reads where the account's drafts folder and sent folder were last found, without asking the mail server.</summary>
    /// <remarks>
    /// A folder the account maps to neither role contributes no tree. One mapped and never bound — never synchronized,
    /// and never resolved since — has no place on the server anything could lie beneath, so it stands for itself alone.
    /// </remarks>
    private async Task<IReadOnlyList<KeptTreeRoot>> ReadKeptTreeRootsAsync(
        MailAccountId account,
        CancellationToken cancellationToken)
    {
        var roots = new List<KeptTreeRoot>(KeptTreeRoles.Length);

        foreach (var role in KeptTreeRoles)
        {
            if (this.folderReferences.TryResolve(account, MailFolderReference.ToRole(role)) is not { } mapping)
            {
                continue;
            }

            var binding = await this.bindings.GetCurrentResolutionAsync(account, mapping.Alias, cancellationToken);

            roots.Add(new KeptTreeRoot(mapping.Alias, binding?.RemotePath));
        }

        return roots;
    }

    /// <summary>The roles whose folders are each the root of a tree that mail is filed neither into nor out of.</summary>
    private static MailFolderSpecialUse[] KeptTreeRoles { get; } = [MailFolderSpecialUse.Drafts, MailFolderSpecialUse.Sent];

    /// <summary>Whether a message in the source folder would cross the edge of a kept tree by being filed into the destination.</summary>
    /// <remarks>
    /// Where a message is is read off the folder it is in rather than off its <c>\Draft</c> flag, because the folder is
    /// what a client opens a message in its composer by, and a mail server files a draft or a sent copy there whichever
    /// client wrote it. The drafts folder and the sent folder each stand for their whole subtree, so mail moves freely
    /// between such a folder and the subfolders somebody made beneath it, and never across the edge of either — except
    /// into the trash, which is how it is deleted.
    /// </remarks>
    private static bool CrossesKeptTree(
        MailFolderResolution source,
        MailboxDestination destination,
        IReadOnlyList<KeptTreeRoot> keptTrees) =>
        destination.Role != MailFolderSpecialUse.Trash
        && TreeOf(source.Alias, source.RemotePath, keptTrees) != TreeOf(destination.Alias, destination.Path, keptTrees);

    /// <summary>Names the kept tree a folder lies in, or nothing where it lies in neither.</summary>
    private static MailFolderAlias? TreeOf(
        MailFolderAlias alias,
        RemoteFolderPath path,
        IReadOnlyList<KeptTreeRoot> keptTrees) =>
        keptTrees
            .Where(root => root.Alias == alias || (root.Path is { } rootPath && path.IsWithin(rootPath)))
            .Select(static root => (MailFolderAlias?)root.Alias)
            .FirstOrDefault();

    /// <summary>The drafts or the sent folder, and where on the server it was last found where it has been.</summary>
    private sealed record KeptTreeRoot(MailFolderAlias Alias, RemoteFolderPath? Path);
}
