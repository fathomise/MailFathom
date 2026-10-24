// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Accounts.Custody;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders;

/// <summary>Creates, renames, moves, and deletes an account's folders, whichever copy of its mailbox is the truth.</summary>
/// <remarks>
/// <para>
/// This is the one folder-management use case. Which custodian carries an act is chosen by
/// <see cref="MailboxCustodianResolver" /> rather than by whoever asked, and a caller states the act and the folder and
/// is told what happened; nothing it receives says where the mail is kept, which is what keeps a client from branching
/// on the storage mode and then being wrong about an account that changed mode since.
/// </para>
/// <para>
/// A folder is named by its identity whichever copy of the mailbox is the truth, which is the identity every other read
/// reports it by. An identity is read as text, and text that names no folder is answered as a folder the account does
/// not have rather than as a malformed request — a caller can only have got such a value from this surface.
/// </para>
/// <para>
/// An account is narrowed to the caller's assignments here, which is the only place it is: the custodians take the
/// account's generated identifier and none of them knows who asked, and the transport surface passes on whatever a
/// request named. An account this caller is not assigned is answered exactly as one the deployment never served, so a
/// refusal never says which mailboxes exist beside their own.
/// </para>
/// </remarks>
public sealed class MailFolderEditor
{
    private readonly MailboxCustodianResolver custodians;
    private readonly AccessAuthorization authorization;
    private readonly ICallerMailAccountCatalog accounts;

    /// <summary>Initializes a new instance of the <see cref="MailFolderEditor" /> class.</summary>
    /// <param name="custodians">Chooses the custodian an account's folders are acted on through.</param>
    /// <param name="authorization">Answers which user the caller acts for.</param>
    /// <param name="accounts">Answers which accounts that user is assigned, which is what an act is narrowed to.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailFolderEditor(
        MailboxCustodianResolver custodians,
        AccessAuthorization authorization,
        ICallerMailAccountCatalog accounts)
    {
        ArgumentNullException.ThrowIfNull(custodians);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(accounts);

        this.custodians = custodians;
        this.authorization = authorization;
        this.accounts = accounts;
    }

    /// <summary>Reads the account's folders and which acts each of them and the account itself allow.</summary>
    /// <param name="account">The account, by its generated identifier.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The folders and their acts, or <see langword="null" /> where this caller is assigned no such account.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <c>mailfathom.mail.read</c>, or acts for no user at all.</exception>
    public async Task<MailFolderManagement?> ReadAsync(MailAccountId account, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        if (!await this.IsAssignedAsync(account, cancellationToken))
        {
            return null;
        }

        var custodian = await this.custodians.ResolveAsync(account, cancellationToken);

        return await custodian.DescribeFoldersAsync(account, cancellationToken);
    }

    /// <summary>Creates a folder, either by naming it or by naming the role it is to play.</summary>
    /// <param name="account">The caller's account.</param>
    /// <param name="parentId">The folder to create it beneath, or <see langword="null" /> for the top of the hierarchy.</param>
    /// <param name="name">The folder's name, which a creation naming a role does not need.</param>
    /// <param name="role">The role the folder is to play, or <see langword="null" /> for an ordinary folder.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The created folder, or the refusal.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <c>mailfathom.mail.folders.write</c> and <c>mailfathom.mail.read</c>, both of which an act needs.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when an act on a mirrored account committed on its mail server and a competing writer kept the folder it left from being given a row.</exception>
    public Task<MailFolderActOutcome> CreateAsync(
        MailAccountId account,
        string? parentId,
        string? name,
        MailFolderSpecialUse? role,
        CancellationToken cancellationToken) =>
        this.ActAsync(
            account,
            custodian => TryReadParent(parentId, out var parent)
                ? custodian.CreateFolderAsync(account, parent, name, role, cancellationToken)
                : Task.FromResult(MailFolderActOutcome.Refused(MailFolderActRefusal.ParentMissing)),
            cancellationToken);

    /// <summary>Renames a folder, leaving it where it is.</summary>
    /// <param name="account">The caller's account.</param>
    /// <param name="folderId">The folder.</param>
    /// <param name="name">The new name.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The renamed folder, or the refusal.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <c>mailfathom.mail.folders.write</c> and <c>mailfathom.mail.read</c>, both of which an act needs.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when an act on a mirrored account committed on its mail server and a competing writer kept the folder it left from being given a row.</exception>
    public Task<MailFolderActOutcome> RenameAsync(
        MailAccountId account,
        string? folderId,
        string? name,
        CancellationToken cancellationToken) =>
        this.ActOnFolderAsync(
            account,
            folderId,
            (custodian, folder) => custodian.RenameFolderAsync(account, folder, name, cancellationToken),
            cancellationToken);

    /// <summary>Moves a folder, with everything beneath it, to another place in the hierarchy.</summary>
    /// <param name="account">The caller's account.</param>
    /// <param name="folderId">The folder.</param>
    /// <param name="parentId">The folder to move it beneath, or <see langword="null" /> for the top of the hierarchy.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The moved folder, or the refusal.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <c>mailfathom.mail.folders.write</c> and <c>mailfathom.mail.read</c>, both of which an act needs.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when an act on a mirrored account committed on its mail server and a competing writer kept the folder it left from being given a row.</exception>
    public Task<MailFolderActOutcome> MoveAsync(
        MailAccountId account,
        string? folderId,
        string? parentId,
        CancellationToken cancellationToken) =>
        this.ActOnFolderAsync(
            account,
            folderId,
            (custodian, folder) => TryReadParent(parentId, out var parent)
                ? custodian.MoveFolderAsync(account, folder, parent, cancellationToken)
                : Task.FromResult(MailFolderActOutcome.Refused(MailFolderActRefusal.ParentMissing)),
            cancellationToken);

    /// <summary>Deletes a folder, which means whatever the account's own rules make it mean.</summary>
    /// <param name="account">The caller's account.</param>
    /// <param name="folderId">The folder.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The folder as the act left it, or the refusal.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <c>mailfathom.mail.folders.write</c> and <c>mailfathom.mail.read</c>, both of which an act needs.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when an act on a mirrored account committed on its mail server and a competing writer kept the folder it left from being given a row.</exception>
    /// <remarks>
    /// On an account whose mailbox MailFathom holds, a deletion moves the folder into the trash and a deletion of one
    /// already there erases it with its mail. On a mirrored account it deletes the folder on the mail server, or marks
    /// it deleted here, according to the account's own deletion setting. The change reported says which of them it was.
    /// </remarks>
    public Task<MailFolderActOutcome> DeleteAsync(
        MailAccountId account,
        string? folderId,
        CancellationToken cancellationToken) =>
        this.ActOnFolderAsync(
            account,
            folderId,
            (custodian, folder) => custodian.DeleteFolderAsync(account, folder, cancellationToken),
            cancellationToken);

    // Absence of a parent is what names the top of the hierarchy, so text that names no folder must not be read as it:
    // the folder would be created or moved somewhere the request never asked for.
    private static bool TryReadParent(string? parentId, out MailFolderId? parent)
    {
        parent = FolderOf(parentId);

        return parent is not null || string.IsNullOrWhiteSpace(parentId);
    }

    private static MailFolderId? FolderOf(string? folderId) =>
        Guid.TryParse(folderId, out var parsed) && parsed != Guid.Empty ? MailFolderId.Create(parsed) : null;

    private Task<MailFolderActOutcome> ActOnFolderAsync(
        MailAccountId account,
        string? folderId,
        Func<IMailboxCustodian, MailFolderId, Task<MailFolderActOutcome>> act,
        CancellationToken cancellationToken) =>
        this.ActAsync(
            account,
            custodian => FolderOf(folderId) is { } folder
                ? act(custodian, folder)
                : Task.FromResult(MailFolderActOutcome.Refused(MailFolderActRefusal.FolderMissing)),
            cancellationToken);

    private async Task<MailFolderActOutcome> ActAsync(
        MailAccountId account,
        Func<IMailboxCustodian, Task<MailFolderActOutcome>> act,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailFoldersWrite);

        if (!await this.IsAssignedAsync(account, cancellationToken))
        {
            return MailFolderActOutcome.Refused(MailFolderActRefusal.AccountMissing);
        }

        // An act reports the folder it left, which is a read of the account's folders.
        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        return await act(await this.custodians.ResolveAsync(account, cancellationToken));
    }

    /// <summary>Answers whether the user this unit of work acts for is assigned the account a read or an act named.</summary>
    /// <remarks>
    /// A principal acting for no user is refused by the catalog rather than answered, because an empty set of
    /// assignments would let that mistake read as a person who is assigned nothing.
    /// </remarks>
    private async Task<bool> IsAssignedAsync(MailAccountId account, CancellationToken cancellationToken) =>
        (await this.accounts.ReadAssignedAccountsAsync(cancellationToken)).Any(assigned => assigned.Id == account);
}
