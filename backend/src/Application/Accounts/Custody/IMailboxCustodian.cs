// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Mail.Mutations.Local;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Accounts.Custody;

/// <summary>Carries every act on one account's mailbox to whichever copy of it is the truth.</summary>
/// <remarks>
/// <para>
/// It has one implementation per custody phase, and <see cref="MailboxCustodianResolver" /> is the only thing that
/// chooses between them, so a caller never branches on where a mailbox's truth lives and an act added here is a compiler
/// error until every phase carries it. See
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0039-one-folder-model-and-one-implementation-per-custody-phase.md">ADR 0039</see>.
/// </para>
/// <para>
/// The transaction each act runs in is part of its contract, and the signatures say which. A folder act takes none and
/// owns its own, because on a mirrored account it reaches the mail server before anything is written and a transaction
/// held across that round trip is what ADR 0007's write session exists to avoid. A change on a message, and the filing
/// of a copy, take the caller's transaction, so the act and the custody it was judged under commit together; what
/// they need before that transaction opens — a destination resolved against a server, a payload placed — is a
/// preparation of its own.
/// </para>
/// <para>
/// Synchronization, the drain, the restore, and the convergence pass are not here: each exists because of a mode rather
/// than independently of one, and they read the phase themselves.
/// </para>
/// </remarks>
public interface IMailboxCustodian
{
    /// <summary>Gets whether the account's folders are local ones, so an arrival or a filed copy is placed in them and a local folder edit commits.</summary>
    /// <remarks>
    /// The writes that act on local folders were dispatched on an earlier reading of the account, so they ask again inside
    /// their own transaction, through
    /// <see cref="MailboxCustodianResolver.ResolveAsync(IPersistenceSession, MailAccountId, CancellationToken)" />, and
    /// write nothing where the custodian they get back answers <see langword="false" />. A restoring account answers
    /// <see langword="false" /> although it commits message acts locally: its folders are being handed back to the source
    /// and take no new ones.
    /// </remarks>
    bool PlacesMailInLocalFolders { get; }

    /// <summary>Reads the account's folders and which acts each of them and the account itself allow.</summary>
    /// <param name="account">The account.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The folders and their acts, or <see langword="null" /> where the deployment holds no such account yet.</returns>
    Task<MailFolderManagement?> DescribeFoldersAsync(MailAccountId account, CancellationToken cancellationToken);

    /// <summary>Creates a folder, either by naming it or by naming the role it is to play.</summary>
    /// <param name="account">The account.</param>
    /// <param name="parent">The folder to create it beneath, or <see langword="null" /> for the top of the hierarchy.</param>
    /// <param name="name">The folder's name, which a creation naming a role does not need.</param>
    /// <param name="role">The role the folder is to play, or <see langword="null" /> for an ordinary folder.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The created folder, or the refusal.</returns>
    Task<MailFolderActOutcome> CreateFolderAsync(
        MailAccountId account,
        MailFolderId? parent,
        string? name,
        MailFolderSpecialUse? role,
        CancellationToken cancellationToken);

    /// <summary>Renames a folder, leaving it where it is.</summary>
    /// <param name="account">The account.</param>
    /// <param name="folder">The folder.</param>
    /// <param name="name">The new name.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The renamed folder, or the refusal.</returns>
    Task<MailFolderActOutcome> RenameFolderAsync(
        MailAccountId account,
        MailFolderId folder,
        string? name,
        CancellationToken cancellationToken);

    /// <summary>Moves a folder, with everything beneath it, to another place in the hierarchy.</summary>
    /// <param name="account">The account.</param>
    /// <param name="folder">The folder.</param>
    /// <param name="parent">The folder to move it beneath, or <see langword="null" /> for the top of the hierarchy.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The moved folder, or the refusal.</returns>
    Task<MailFolderActOutcome> MoveFolderAsync(
        MailAccountId account,
        MailFolderId folder,
        MailFolderId? parent,
        CancellationToken cancellationToken);

    /// <summary>Deletes a folder, which means whatever the account's own rules make it mean.</summary>
    /// <param name="account">The account.</param>
    /// <param name="folder">The folder.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The folder as the act left it, or the refusal.</returns>
    Task<MailFolderActOutcome> DeleteFolderAsync(
        MailAccountId account,
        MailFolderId folder,
        CancellationToken cancellationToken);

    /// <summary>Answers what a folder the account maps but does not synchronize means as a destination, before the transaction a change commits in.</summary>
    /// <param name="account">The account.</param>
    /// <param name="mapping">The folder's mapping.</param>
    /// <param name="cancellationToken">Cancels a resolution that reaches the mail server.</param>
    /// <returns>The folder, or why there is none.</returns>
    Task<MailboxDestinationResolution> ResolveUnsynchronizedDestinationAsync(
        MailAccountId account,
        MailFolderMapping mapping,
        CancellationToken cancellationToken);

    /// <summary>Places the payload of every copy a batch asks for, before the transaction that commits them opens.</summary>
    /// <param name="account">The account the batch acts on.</param>
    /// <param name="sources">The message each planned copy duplicates, one entry per copy.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The placed copies, which are none where a copy is a command for the server rather than a second stored message.</returns>
    Task<PreparedLocalCopies> PrepareCopiesAsync(
        MailAccountId account,
        IReadOnlyCollection<StoredEmailId> sources,
        CancellationToken cancellationToken);

    /// <summary>Records or commits one change on a message, inside the caller's transaction.</summary>
    /// <param name="session">The transaction the change commits in.</param>
    /// <param name="change">The change.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The record written, the local change committed, or why neither happened.</returns>
    Task<SubmittedMailboxChange> SubmitAsync(
        IPersistenceSession session,
        MailboxChange change,
        CancellationToken cancellationToken);

    /// <summary>Prepares the local copy of a draft revision, before the transaction that writes the revision and files it.</summary>
    /// <param name="account">The account the draft belongs to.</param>
    /// <param name="rawMime">The revision's message.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The prepared copy, or <see langword="null" /> where the revision is filed on the server by <see cref="SettleDraftAsync" /> instead, or no bound folder plays the drafts role.</returns>
    /// <remarks>
    /// Only a local custodian prepares one, and <see cref="LocalMailFiler.FileAsync" /> files it only once the resolver,
    /// asked again inside that transaction, still answers with the held custodian.
    /// </remarks>
    Task<LocalMailCopy?> PrepareDraftCopyAsync(
        MailAccountId account,
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken);

    /// <summary>Does whatever one draft's recorded stage says its copy is owed — files its current revision or gives it up.</summary>
    /// <param name="draft">The draft, read with its copies.</param>
    /// <param name="cancellationToken">Cancels the commands and the writes around them.</param>
    /// <returns>What the attempt did, which is already durable by the time it is returned.</returns>
    /// <remarks>
    /// A failure the attempt does not classify itself is raised; <see cref="MailDraftFiler.SettleAsync" /> is the entry
    /// that records it against the draft.
    /// </remarks>
    Task<MailDraftFilingResult> SettleDraftAsync(MailDraftRecord draft, CancellationToken cancellationToken);

    /// <summary>Prepares the sent copy of a delivered message, before the transaction that records the delivery and files it.</summary>
    /// <param name="send">The send that was accepted.</param>
    /// <param name="cancellationToken">Stops the preparation when the host is shutting down.</param>
    /// <returns>The prepared copy, or <see langword="null" /> where the copy is filed on the server afterwards instead, or none could be prepared.</returns>
    /// <remarks>
    /// A failure is raised; <see cref="LocalMailFiler.PrepareSentCopyAsync" /> is the entry that records it against the
    /// send, because the delivery the copy belongs to has already reached its recipients.
    /// </remarks>
    Task<LocalMailCopy?> PrepareSentCopyAsync(OutgoingEmailRecord send, CancellationToken cancellationToken);

    /// <summary>Brings the server copies of one settled send up to date by filing its sent copy.</summary>
    /// <param name="send">The send, read after its attempt ended.</param>
    /// <param name="cancellationToken">Cancels the appends.</param>
    /// <returns>What each attempt did, which is empty where no copy of a send is kept on a server.</returns>
    Task<IReadOnlyList<OutgoingMailFilingResult>> SettleFiledCopiesAsync(
        OutgoingEmailRecord send,
        CancellationToken cancellationToken);

    /// <summary>Takes back the sent copies whose provider has since filed one of its own beside them.</summary>
    /// <param name="account">The account.</param>
    /// <param name="cancellationToken">Cancels the read and the withdrawals.</param>
    /// <returns>What each withdrawal did, which is empty where no copy of a send is kept on a server.</returns>
    Task<IReadOnlyList<OutgoingMailFilingResult>> WithdrawDuplicatedSentCopiesAsync(
        MailAccountId account,
        CancellationToken cancellationToken);
}
