// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>Narrows a query by the folders a caller selected, or by the folders configuration admits or withholds.</summary>
/// <remarks>
/// <para>
/// The two kinds of narrowing name a folder differently, because they come from different places. A caller selects a
/// folder by its MailFathom-issued identity or by the role it plays: an identity names one account's folder by itself,
/// so <see cref="Selecting(IQueryable{StoredEmailEntity}, IReadOnlyList{MailFolderId}, IReadOnlyList{MailFolderSpecialUse})" />
/// narrows by the folder reference with no account clause, and a role is matched against the role each account's own
/// folder row plays, so one role reaches each account's own folder.
/// </para>
/// <para>
/// Configuration decides by an account and an alias together, because the alias is what per-folder settings —
/// participation, junk, visibility to AI — are keyed by, and it is matched against the alias recorded on the folder
/// row. One clause is composed per account the decision reaches — <c>account &lt;&gt; 'a' OR alias &lt;&gt; ALL(…)</c>
/// to withhold, <c>account &lt;&gt; 'a' OR alias = ANY(…)</c> to admit — which is bounded by how many accounts are
/// configured. Narrowing by the alias alone would withhold or admit another account's folder of the same name.
/// </para>
/// <para>
/// A folder whose row carries no alias is one no declaration names: a folder a person made on a held account.
/// <see cref="Admitting(IQueryable{StoredEmailEntity}, IReadOnlyList{MailFolderIdentity})" /> admits it within the
/// accounts admitted at all, and <see cref="Excluding(IQueryable{StoredEmailEntity}, IReadOnlyList{MailFolderIdentity})" />
/// never removes it, because no setting can name it to withhold it, and withholding it by default would hide every
/// folder a person made from the tools they made it for. A folder whose row still carries an alias configuration no
/// longer declares is the opposite case and stays out, because nothing admits it.
/// </para>
/// <para>
/// What an empty list means follows from what each list is. A withheld set that is empty withholds nothing and a
/// selection that is empty is a caller who named no folder, so both disappear and the query is unnarrowed; an admitted
/// set that is empty admits nothing, because an account is readable by being admitted rather than by not being
/// mentioned.
/// </para>
/// </remarks>
internal static class AccountScopedMailFolders
{
    /// <summary>Narrows stored emails to the ones outside the excluded folders.</summary>
    /// <param name="emails">The emails to narrow.</param>
    /// <param name="excluded">The folders to leave out, empty for none.</param>
    /// <returns>The narrowed query, which PostgreSQL evaluates in full.</returns>
    internal static IQueryable<StoredEmailEntity> Excluding(
        IQueryable<StoredEmailEntity> emails,
        IReadOnlyList<MailFolderIdentity> excluded)
    {
        foreach (var (accountId, aliases) in AliasesByAccount(excluded))
        {
            emails = emails.Where(email =>
                email.MailboxAccountId != accountId
                || email.Folder.Alias == null
                || !aliases.Contains(email.Folder.Alias));
        }

        return emails;
    }

    /// <summary>Narrows folder bindings to the ones outside the excluded folders.</summary>
    /// <param name="folders">The bindings to narrow.</param>
    /// <param name="excluded">The folders to leave out, empty for none.</param>
    /// <returns>The narrowed query, which PostgreSQL evaluates in full.</returns>
    internal static IQueryable<MailFolderBindingEntity> Excluding(
        IQueryable<MailFolderBindingEntity> folders,
        IReadOnlyList<MailFolderIdentity> excluded)
    {
        foreach (var (accountId, aliases) in AliasesByAccount(excluded))
        {
            folders = folders.Where(folder =>
                folder.MailboxAccountId != accountId || !aliases.Contains(folder.Alias));
        }

        return folders;
    }

    /// <summary>Narrows stored emails to the folders configuration admits, each within its own account.</summary>
    /// <param name="emails">The emails to narrow.</param>
    /// <param name="admitted">The folders a read may return mail from, empty when none may.</param>
    /// <returns>The narrowed query, which PostgreSQL evaluates in full.</returns>
    /// <remarks>
    /// The same shape as <see cref="Selecting(IQueryable{StoredEmailEntity}, IReadOnlyList{MailFolderId}, IReadOnlyList{MailFolderSpecialUse})" /> and
    /// one difference that is the whole point of having two: an empty list admits nothing rather than everything. A
    /// caller's folder filter is absent when they named no folder, and configuration's admission is empty when the
    /// deployment maps no folder the reader may reach — which has to mean no mail rather than all of it, since the store
    /// holds rows of folders no mapping names any more.
    /// </remarks>
    internal static IQueryable<StoredEmailEntity> Admitting(
        IQueryable<StoredEmailEntity> emails,
        IReadOnlyList<MailFolderIdentity> admitted)
    {
        var admittedAccounts = AccountsOf(admitted);
        emails = emails.Where(email => admittedAccounts.Contains(email.MailboxAccountId));

        foreach (var (accountId, aliases) in AliasesByAccount(admitted))
        {
            emails = emails.Where(email =>
                email.MailboxAccountId != accountId
                || email.Folder.Alias == null
                || aliases.Contains(email.Folder.Alias));
        }

        return emails;
    }

    /// <summary>Narrows folder bindings to the folders configuration admits, read the same way as above.</summary>
    /// <param name="folders">The bindings to narrow.</param>
    /// <param name="admitted">The folders a read may report on, empty when none may.</param>
    /// <returns>The narrowed query, which PostgreSQL evaluates in full.</returns>
    internal static IQueryable<MailFolderBindingEntity> Admitting(
        IQueryable<MailFolderBindingEntity> folders,
        IReadOnlyList<MailFolderIdentity> admitted)
    {
        var admittedAccounts = AccountsOf(admitted);
        folders = folders.Where(folder => admittedAccounts.Contains(folder.MailboxAccountId));

        foreach (var (accountId, aliases) in AliasesByAccount(admitted))
        {
            folders = folders.Where(folder =>
                folder.MailboxAccountId != accountId || aliases.Contains(folder.Alias));
        }

        return folders;
    }

    /// <summary>Narrows stored emails to the folders a scope selected, by their identity or by the role they play.</summary>
    /// <param name="emails">The emails to narrow.</param>
    /// <param name="selectedFolders">The folders to keep, empty when the request named none.</param>
    /// <param name="selectedRoles">The roles whose folders to keep, empty when the request named none.</param>
    /// <returns>The narrowed query, which PostgreSQL evaluates in full.</returns>
    /// <remarks>
    /// A folder identity names one account's folder, so it needs no account clause of its own; a role is read against
    /// the role each account's own folder plays. Both empty keeps every folder.
    /// </remarks>
    internal static IQueryable<StoredEmailEntity> Selecting(
        IQueryable<StoredEmailEntity> emails,
        IReadOnlyList<MailFolderId> selectedFolders,
        IReadOnlyList<MailFolderSpecialUse> selectedRoles)
    {
        if (selectedFolders.Count == 0 && selectedRoles.Count == 0)
        {
            return emails;
        }

        var folderIds = FolderIdsOf(selectedFolders);
        MailFolderSpecialUse?[] roles = [.. selectedRoles.Select(static role => (MailFolderSpecialUse?)role)];

        return emails.Where(email => folderIds.Contains(email.FolderId) || roles.Contains(email.Folder.Role));
    }

    /// <summary>Narrows folder bindings to the folders a scope selected, read the same way as above.</summary>
    /// <param name="folders">The bindings to narrow.</param>
    /// <param name="selectedFolders">The folders to keep, empty when the request named none.</param>
    /// <param name="selectedRoles">The roles whose folders to keep, empty when the request named none.</param>
    /// <returns>The narrowed query, which PostgreSQL evaluates in full.</returns>
    internal static IQueryable<MailFolderBindingEntity> Selecting(
        IQueryable<MailFolderBindingEntity> folders,
        IReadOnlyList<MailFolderId> selectedFolders,
        IReadOnlyList<MailFolderSpecialUse> selectedRoles)
    {
        if (selectedFolders.Count == 0 && selectedRoles.Count == 0)
        {
            return folders;
        }

        Guid?[] folderIds = [.. FolderIdsOf(selectedFolders).Select(static id => (Guid?)id)];
        MailFolderSpecialUse?[] roles = [.. selectedRoles.Select(static role => (MailFolderSpecialUse?)role)];

        return folders.Where(folder => folderIds.Contains(folder.FolderId)
            || (folder.Folder != null && roles.Contains(folder.Folder.Role)));
    }

    /// <summary>Narrows folder rows to the folders a scope selected, read the same way as above.</summary>
    /// <param name="folders">The folder rows to narrow.</param>
    /// <param name="selectedFolders">The folders to keep, empty when the request named none.</param>
    /// <param name="selectedRoles">The roles whose folders to keep, empty when the request named none.</param>
    /// <returns>The narrowed query, which PostgreSQL evaluates in full.</returns>
    internal static IQueryable<MailFolderEntity> Selecting(
        IQueryable<MailFolderEntity> folders,
        IReadOnlyList<MailFolderId> selectedFolders,
        IReadOnlyList<MailFolderSpecialUse> selectedRoles)
    {
        if (selectedFolders.Count == 0 && selectedRoles.Count == 0)
        {
            return folders;
        }

        var folderIds = FolderIdsOf(selectedFolders);
        MailFolderSpecialUse?[] roles = [.. selectedRoles.Select(static role => (MailFolderSpecialUse?)role)];

        return folders.Where(folder => folderIds.Contains(folder.Id) || roles.Contains(folder.Role));
    }

    /// <summary>Narrows folder rows to the folders configuration admits, read the way stored emails are.</summary>
    /// <param name="folders">The folder rows to narrow.</param>
    /// <param name="admitted">The folders a read may report on, empty when none may.</param>
    /// <returns>The narrowed query, which PostgreSQL evaluates in full.</returns>
    /// <remarks>A folder no declaration names is admitted on an admitted account, because no setting withholds it.</remarks>
    internal static IQueryable<MailFolderEntity> Admitting(
        IQueryable<MailFolderEntity> folders,
        IReadOnlyList<MailFolderIdentity> admitted)
    {
        var admittedAccounts = AccountsOf(admitted);
        folders = folders.Where(folder => admittedAccounts.Contains(folder.MailboxAccountId));

        foreach (var (accountId, aliases) in AliasesByAccount(admitted))
        {
            folders = folders.Where(folder =>
                folder.MailboxAccountId != accountId || folder.Alias == null || aliases.Contains(folder.Alias));
        }

        return folders;
    }

    /// <summary>Narrows folder rows to the ones outside the excluded folders.</summary>
    /// <param name="folders">The folder rows to narrow.</param>
    /// <param name="excluded">The folders to leave out, empty for none.</param>
    /// <returns>The narrowed query, which PostgreSQL evaluates in full.</returns>
    internal static IQueryable<MailFolderEntity> Excluding(
        IQueryable<MailFolderEntity> folders,
        IReadOnlyList<MailFolderIdentity> excluded)
    {
        foreach (var (accountId, aliases) in AliasesByAccount(excluded))
        {
            folders = folders.Where(folder =>
                folder.MailboxAccountId != accountId || folder.Alias == null || !aliases.Contains(folder.Alias));
        }

        return folders;
    }

    /// <summary>Reports whether a set of folder decisions names one folder.</summary>
    /// <param name="decided">The folders the decision reaches, whichever direction it runs in.</param>
    /// <param name="accountId">The account the folder belongs to.</param>
    /// <param name="folderAlias">MailFathom's own name for the folder.</param>
    /// <returns><see langword="true" /> when the set names that pair.</returns>
    internal static bool Contains(
        IReadOnlyList<MailFolderIdentity> decided,
        string accountId,
        string folderAlias) => decided.Any(folder =>
            StringComparer.Ordinal.Equals(folder.AccountId.Value, accountId)
            && StringComparer.Ordinal.Equals(folder.Alias.Value, folderAlias));

    private static Guid[] FolderIdsOf(IReadOnlyList<MailFolderId> folders) =>
        [.. folders.Select(static folder => folder.Value)];

    private static string[] AccountsOf(IReadOnlyList<MailFolderIdentity> folders) =>
    [
        .. folders
            .Select(static folder => folder.AccountId.Value)
            .Distinct(StringComparer.Ordinal),
    ];

    internal static IEnumerable<(string AccountId, string[] Aliases)> AliasesByAccount(
        IReadOnlyList<MailFolderIdentity> excluded) => excluded
            .GroupBy(folder => folder.AccountId.Value, StringComparer.Ordinal)
            .Select(static account => (account.Key, Aliases: account.Select(static folder => folder.Alias.Value).ToArray()));
}
