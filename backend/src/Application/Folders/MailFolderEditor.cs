// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Folders.Local;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders;

/// <summary>Creates, renames, moves, and deletes an account's folders, whichever copy of its mailbox is the truth.</summary>
/// <remarks>
/// <para>
/// This is the one folder-management use case, and which of the two editors beneath it carries an act is decided here
/// from the account's phase rather than by whoever asked. A caller states the act and the folder and is told what
/// happened; nothing it receives says where the mail is kept, which is what keeps a client from branching on the
/// storage mode and then being wrong about an account that changed mode since.
/// </para>
/// <para>
/// A folder is named by its identity whichever copy of the mailbox is the truth, which is the identity every other read
/// reports it by. On a mirrored account the act is carried out on the folder declared under that row's alias, so a
/// folder no alias feeds — one between declared folders on the server — is answered as one the account does not have.
/// An identity is read as text, and text that names no folder is answered the same way rather than as a malformed
/// request — a caller can only have got such a value from this surface.
/// </para>
/// <para>
/// An account whose mailbox is being restored to its source allows nothing. Its local folders are still the truth and
/// its source is being filled back up from them, so an act on either side would be an act against a mailbox that is
/// half in each place; the acts come back when the restore ends.
/// </para>
/// <para>
/// An account is narrowed to the caller's assignments here, which is the only place it is: both editors beneath take
/// the account's generated identifier and neither knows who asked, and the transport surface passes on whatever a
/// request named. An account this caller is not assigned is answered exactly as one the deployment never served, so a
/// refusal never says which mailboxes exist beside their own.
/// </para>
/// </remarks>
public sealed class MailFolderEditor
{
    private readonly LocalMailFolderEditor local;
    private readonly MirroredMailFolderEditor mirrored;
    private readonly AccessAuthorization authorization;
    private readonly ICallerMailAccountCatalog accounts;
    private readonly ILocalMailFolderStore folders;

    /// <summary>Initializes a new instance of the <see cref="MailFolderEditor" /> class.</summary>
    /// <param name="local">Acts on the folders of an account whose mailbox MailFathom holds.</param>
    /// <param name="mirrored">Acts on the folders of an account whose mail server is the truth.</param>
    /// <param name="authorization">Answers which user the caller acts for.</param>
    /// <param name="accounts">Answers which accounts that user is assigned, which is what an act is narrowed to.</param>
    /// <param name="folders">Answers which folder of a mirrored account an identity names.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailFolderEditor(
        LocalMailFolderEditor local,
        MirroredMailFolderEditor mirrored,
        AccessAuthorization authorization,
        ICallerMailAccountCatalog accounts,
        ILocalMailFolderStore folders)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(mirrored);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(folders);

        this.local = local;
        this.mirrored = mirrored;
        this.authorization = authorization;
        this.accounts = accounts;
        this.folders = folders;
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

        var holding = await this.local.ReadAsync(account, cancellationToken);

        if (holding is null)
        {
            return null;
        }

        return holding.Phase is MailAccountCustodyPhase.Mirrored
            ? Identified(
                await this.mirrored.ReadAsync(account, cancellationToken),
                IdsByAlias(await this.folders.ReadFoldersAsync(account, cancellationToken)))
            : DescribeHeld(holding);
    }

    /// <summary>Creates a folder, either by naming it or by naming the role it is to play.</summary>
    /// <param name="account">The caller's account.</param>
    /// <param name="parentId">The folder to create it beneath, or <see langword="null" /> for the top of the hierarchy.</param>
    /// <param name="name">The folder's name, which a creation naming a role does not need.</param>
    /// <param name="role">The role the folder is to play, or <see langword="null" /> for an ordinary folder.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The created folder, or the refusal.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <c>mailfathom.mail.folders.write</c> and <c>mailfathom.mail.read</c>, both of which an act needs.</exception>
    public Task<MailFolderActOutcome> CreateAsync(
        MailAccountId account,
        string? parentId,
        string? name,
        MailFolderSpecialUse? role,
        CancellationToken cancellationToken) =>
        this.ActAsync(
            account,
            holding => this.CreateLocallyAsync(account, holding, parentId, name, role, cancellationToken),
            (identity, rows) => TryReadParentAlias(rows, parentId, out var parent)
                ? this.mirrored.CreateAsync(identity, parent, name, role, cancellationToken)
                : Task.FromResult(MailFolderActOutcome.Refused(MailFolderActRefusal.ParentMissing)),
            cancellationToken);

    /// <summary>Renames a folder, leaving it where it is.</summary>
    /// <param name="account">The caller's account.</param>
    /// <param name="folderId">The folder.</param>
    /// <param name="name">The new name.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The renamed folder, or the refusal.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <c>mailfathom.mail.folders.write</c> and <c>mailfathom.mail.read</c>, both of which an act needs.</exception>
    public Task<MailFolderActOutcome> RenameAsync(
        MailAccountId account,
        string? folderId,
        string? name,
        CancellationToken cancellationToken) =>
        this.ActOnFolderAsync(
            account,
            folderId,
            folder => this.local.RenameAsync(account, folder, name, cancellationToken),
            (identity, alias, _) => this.mirrored.RenameAsync(identity, alias, name, cancellationToken),
            cancellationToken);

    /// <summary>Moves a folder, with everything beneath it, to another place in the hierarchy.</summary>
    /// <param name="account">The caller's account.</param>
    /// <param name="folderId">The folder.</param>
    /// <param name="parentId">The folder to move it beneath, or <see langword="null" /> for the top of the hierarchy.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The moved folder, or the refusal.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <c>mailfathom.mail.folders.write</c> and <c>mailfathom.mail.read</c>, both of which an act needs.</exception>
    public Task<MailFolderActOutcome> MoveAsync(
        MailAccountId account,
        string? folderId,
        string? parentId,
        CancellationToken cancellationToken) =>
        this.ActOnFolderAsync(
            account,
            folderId,
            folder => TryReadParent(parentId, out var parent)
                ? this.local.MoveAsync(account, folder, parent, cancellationToken)
                : Task.FromResult(LocalMailFolderEditOutcome.Refused(MailFolderActRefusal.ParentMissing)),
            (identity, alias, rows) => TryReadParentAlias(rows, parentId, out var parent)
                ? this.mirrored.MoveAsync(identity, alias, parent, cancellationToken)
                : Task.FromResult(MailFolderActOutcome.Refused(MailFolderActRefusal.ParentMissing)),
            cancellationToken);

    /// <summary>Deletes a folder, which means whatever the account's own rules make it mean.</summary>
    /// <param name="account">The caller's account.</param>
    /// <param name="folderId">The folder.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The folder as the act left it, or the refusal.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <c>mailfathom.mail.folders.write</c> and <c>mailfathom.mail.read</c>, both of which an act needs.</exception>
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
            folder => this.local.DeleteAsync(account, folder, cancellationToken),
            (identity, alias, _) => this.mirrored.DeleteAsync(identity, alias, cancellationToken),
            cancellationToken);

    private async Task<MailFolderActOutcome> ActAsync(
        MailAccountId account,
        Func<LocalMailFolderHolding, Task<MailFolderActOutcome>> held,
        Func<MailAccountId, IReadOnlyList<LocalMailFolder>, Task<MailFolderActOutcome>> mirroredAct,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailFoldersWrite);

        if (!await this.IsAssignedAsync(account, cancellationToken))
        {
            return MailFolderActOutcome.Refused(MailFolderActRefusal.AccountMissing);
        }

        var holding = await this.local.ReadAsync(account, cancellationToken);

        return holding switch
        {
            null => MailFolderActOutcome.Refused(MailFolderActRefusal.AccountMissing),
            { Phase: MailAccountCustodyPhase.Mirrored } => await this.ActOnMirroredAsync(account, mirroredAct, cancellationToken),
            { Phase: MailAccountCustodyPhase.Held } => await held(holding),
            _ => MailFolderActOutcome.Refused(MailFolderActRefusal.AccountNotHeld),
        };
    }

    private Task<MailFolderActOutcome> ActOnFolderAsync(
        MailAccountId account,
        string? folderId,
        Func<MailFolderId, Task<LocalMailFolderEditOutcome>> held,
        Func<MailAccountId, MailFolderAlias, IReadOnlyList<LocalMailFolder>, Task<MailFolderActOutcome>> mirroredAct,
        CancellationToken cancellationToken) =>
        this.ActAsync(
            account,
            async holding => LocalFolderOf(folderId) is { } folder
                ? Describe(await held(folder), holding)
                : MailFolderActOutcome.Refused(MailFolderActRefusal.FolderMissing),
            (identity, rows) => AliasIn(rows, folderId) is { } alias
                ? mirroredAct(identity, alias, rows)
                : Task.FromResult(MailFolderActOutcome.Refused(MailFolderActRefusal.FolderMissing)),
            cancellationToken);

    /// <summary>Carries out an act on a mirrored account, and reports the folder it left by the identity a client names it by.</summary>
    /// <remarks>
    /// The rows are read on both sides of the act: a created folder has its row only afterwards, and a deleted one only
    /// before.
    /// </remarks>
    private async Task<MailFolderActOutcome> ActOnMirroredAsync(
        MailAccountId account,
        Func<MailAccountId, IReadOnlyList<LocalMailFolder>, Task<MailFolderActOutcome>> act,
        CancellationToken cancellationToken)
    {
        var before = await this.folders.ReadFoldersAsync(account, cancellationToken);
        var outcome = await act(account, before);

        if (outcome.Folder is not { } folder)
        {
            return outcome;
        }

        var after = await this.folders.ReadFoldersAsync(account, CancellationToken.None);

        return outcome with { Folder = Identified(folder, IdsByAlias([.. before, .. after])) ?? folder };
    }

    private async Task<MailFolderActOutcome> CreateLocallyAsync(
        MailAccountId account,
        LocalMailFolderHolding holding,
        string? parentId,
        string? name,
        MailFolderSpecialUse? role,
        CancellationToken cancellationToken)
    {
        // Every role a held account has a folder for is supplied the moment its hierarchy is read, so a creation naming
        // one is asking for a folder that is already there rather than for something this could make.
        if (role is not null)
        {
            return MailFolderActOutcome.Refused(MailFolderActRefusal.RoleAlreadyPlayed);
        }

        if (!TryReadParent(parentId, out var parent))
        {
            return MailFolderActOutcome.Refused(MailFolderActRefusal.ParentMissing);
        }

        return Describe(await this.local.CreateAsync(account, parent, name, cancellationToken), holding);
    }

    /// <summary>Answers whether the user this unit of work acts for is assigned the account a read or an act named.</summary>
    /// <remarks>
    /// A principal acting for no user is refused by the catalog rather than answered, because an empty set of
    /// assignments would let that mistake read as a person who is assigned nothing.
    /// </remarks>
    private async Task<bool> IsAssignedAsync(MailAccountId account, CancellationToken cancellationToken) =>
        (await this.accounts.ReadAssignedAccountsAsync(cancellationToken)).Any(assigned => assigned.Id == account);

    private static MailFolderManagement DescribeHeld(LocalMailFolderHolding holding)
    {
        var folders = holding.Folders;
        var mayCreate = holding.Phase is MailAccountCustodyPhase.Held
            && folders.Count < LocalMailFolderTree.MaximumFolders;

        return new MailFolderManagement(
            mayCreate ? [MailFolderAct.Create] : [],
            // A held account is given every role it needs the moment its hierarchy is read, and an archive there is an
            // ordinary folder somebody makes, so no role is ever waiting to be created.
            [],
            [
                .. folders
                    .Select(folder => Describe(folder, holding.Phase))
                    .OrderBy(static folder => folder.Name, StringComparer.OrdinalIgnoreCase),
            ]);
    }

    private static ManagedMailFolder Describe(LocalMailFolder folder, MailAccountCustodyPhase phase) => new(
        folder.Id.Value.ToString(),
        folder.ParentId?.Value.ToString(),
        folder.Name.Value,
        folder.Role,
        folder.IsProtected || phase is not MailAccountCustodyPhase.Held
            ? []
            : [MailFolderAct.Rename, MailFolderAct.Move, MailFolderAct.Delete]);

    private static MailFolderActOutcome Describe(LocalMailFolderEditOutcome outcome, LocalMailFolderHolding holding) =>
        outcome is { Folder: { } folder, Kind: { } kind }
            ? new MailFolderActOutcome(Describe(folder, holding.Phase), kind, Refusal: null, outcome.MailErasureDeferred)
            : MailFolderActOutcome.Refused(
                outcome.Refusal ?? throw new InvalidOperationException("An edit outcome carried neither a folder nor a refusal."));

    // Absence of a parent is what names the top of the hierarchy, so text that names no folder must not be read as it:
    // the folder would be created or moved somewhere the request never asked for. The two readings differ only in what
    // an identity looks like on each side — an identifier a held account issued, an alias a mirrored one declares.
    private static bool TryReadParent(string? parentId, out MailFolderId? parent)
    {
        parent = LocalFolderOf(parentId);

        return parent is not null || string.IsNullOrWhiteSpace(parentId);
    }

    private static bool TryReadParentAlias(
        IReadOnlyList<LocalMailFolder> rows,
        string? parentId,
        out MailFolderAlias? parent)
    {
        parent = AliasIn(rows, parentId);

        return parent is not null || string.IsNullOrWhiteSpace(parentId);
    }

    private static MailFolderId? LocalFolderOf(string? folderId) =>
        Guid.TryParse(folderId, out var parsed) && parsed != Guid.Empty ? MailFolderId.Create(parsed) : null;

    private static MailFolderAlias? AliasIn(IReadOnlyList<LocalMailFolder> rows, string? folderId) =>
        LocalFolderOf(folderId) is { } id ? rows.FirstOrDefault(row => row.Id == id)?.SourceFolderAlias : null;

    private static Dictionary<MailFolderAlias, string> IdsByAlias(IEnumerable<LocalMailFolder> rows) =>
        rows
            .Where(static row => row.SourceFolderAlias is not null)
            .GroupBy(static row => (MailFolderAlias)row.SourceFolderAlias!)
            .ToDictionary(static group => group.Key, static group => group.Last().Id.ToString());

    /// <summary>Names the mirrored editor's folders by their identities, leaving out one whose row the account's next run writes.</summary>
    private static MailFolderManagement Identified(
        MailFolderManagement management,
        Dictionary<MailFolderAlias, string> ids) =>
        management with
        {
            Folders = [.. management.Folders.Select(folder => Identified(folder, ids)).OfType<ManagedMailFolder>()],
        };

    private static ManagedMailFolder? Identified(ManagedMailFolder folder, Dictionary<MailFolderAlias, string> ids) =>
        MailFolderAlias.TryCreate(folder.Id, out var alias) && ids.TryGetValue(alias, out var id)
            ? folder with
            {
                Id = id,
                ParentId = MailFolderAlias.TryCreate(folder.ParentId, out var parent)
                    && ids.TryGetValue(parent, out var parentId)
                        ? parentId
                        : null,
            }
            : null;
}
