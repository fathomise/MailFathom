// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Folders;

/// <summary>Keeps a mirrored account's folder row for one bound alias in the shape its source folder has.</summary>
/// <remarks>
/// <para>
/// On a mirrored account the source server is the truth and the folder rows are its projection: the row an alias names
/// carries the last level of the remote path as its name, sits under the row of the remote parent, and plays the role
/// configuration declares for the alias. A remote ancestor no alias names still gets a row, carrying no alias, so the
/// hierarchy a reader sees is the server's rather than a flat list of whatever happens to be declared.
/// </para>
/// <para>
/// Everything here works on the rows it is handed and mints nothing outside them, so one call is a pure function of the
/// account's live folders, its current bindings, and the binding being projected. What it changes is what the caller
/// commits; projecting a folder already in shape changes nothing.
/// </para>
/// </remarks>
internal static class MailFolderProjection
{
    /// <summary>Brings the row of one bound alias, and the rows of its remote ancestors, into the shape of the source folder.</summary>
    /// <param name="liveFolders">The account's live folder rows, to which every row this call creates is appended.</param>
    /// <param name="aliasesByRemotePath">The alias each current binding of the account names, keyed by its remote path.</param>
    /// <param name="binding">The binding being projected.</param>
    /// <param name="role">The role configuration declares for the alias, or <see langword="null" /> for none.</param>
    /// <param name="mintFolderId">Mints the identifier of a row this call creates.</param>
    /// <returns>The row the alias names.</returns>
    internal static MailFolderEntity Project(
        List<MailFolderEntity> liveFolders,
        IReadOnlyDictionary<string, string> aliasesByRemotePath,
        MailFolderBindingEntity binding,
        MailFolderSpecialUse? role,
        Func<Guid> mintFolderId)
    {
        ArgumentNullException.ThrowIfNull(liveFolders);
        ArgumentNullException.ThrowIfNull(aliasesByRemotePath);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(mintFolderId);

        var levels = LevelsOf(binding);
        var parentId = ProjectAncestors(liveFolders, aliasesByRemotePath, binding, levels, mintFolderId);
        var name = NameFor(levels[^1], binding.Alias);
        var folder = liveFolders.FirstOrDefault(row => row.Alias == binding.Alias)
            ?? FindUnnamedSibling(liveFolders, parentId, name)
            ?? Added(liveFolders, binding.MailboxAccountId, parentId, name, mintFolderId);

        folder.Alias = binding.Alias;
        folder.ParentId = WouldLoop(liveFolders, folder.Id, parentId) ? null : parentId;
        Rename(folder, liveFolders, name, binding.Alias);
        TakeRole(folder, liveFolders, role);

        return folder;
    }

    /// <summary>Gives a declared alias a folder row before anything has bound it to a source folder.</summary>
    /// <param name="liveFolders">The account's live folder rows, to which a row this call creates is appended.</param>
    /// <param name="accountId">The account the alias is declared on.</param>
    /// <param name="alias">The declared alias.</param>
    /// <param name="declaredPath">The source path the declaration names, or <see langword="null" /> for one that names a role.</param>
    /// <param name="role">The role the declaration labels the folder with, or <see langword="null" /> for none.</param>
    /// <param name="mintFolderId">Mints the identifier of a row this call creates.</param>
    /// <returns>The row created, or <see langword="null" /> when the alias already names one.</returns>
    /// <remarks>
    /// A folder an operator declared and asked not to mirror is never bound by a synchronization run, and is still a
    /// folder of the account that mail is filed into. Its row sits at the top of the hierarchy under the declared path's
    /// text until a binding gives it its real place and name.
    /// </remarks>
    internal static MailFolderEntity? Declare(
        List<MailFolderEntity> liveFolders,
        string accountId,
        string alias,
        string? declaredPath,
        MailFolderSpecialUse? role,
        Func<Guid> mintFolderId)
    {
        ArgumentNullException.ThrowIfNull(liveFolders);
        ArgumentNullException.ThrowIfNull(mintFolderId);

        if (liveFolders.Any(row => row.Alias == alias))
        {
            return null;
        }

        var name = NameFor(declaredPath ?? alias, alias);
        var folder = FindUnnamedSibling(liveFolders, parentId: null, name)
            ?? Added(liveFolders, accountId, parentId: null, name, mintFolderId);

        folder.Alias = alias;
        Rename(folder, liveFolders, folder.Name, alias);

        if (role is { } declared && !liveFolders.Any(row => row.Role == declared))
        {
            folder.Role = declared;
        }

        return folder;
    }

    private static string[] LevelsOf(MailFolderBindingEntity binding) =>
        binding.HierarchyDelimiter is { Length: 1 } delimiter
            ? binding.RemotePath.Split(delimiter[0], StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } levels
                ? levels
                : [binding.RemotePath]
            : [binding.RemotePath];

    private static Guid? ProjectAncestors(
        List<MailFolderEntity> liveFolders,
        IReadOnlyDictionary<string, string> aliasesByRemotePath,
        MailFolderBindingEntity binding,
        string[] levels,
        Func<Guid> mintFolderId)
    {
        Guid? parentId = null;

        for (var depth = 1; depth < levels.Length; depth++)
        {
            var ancestorPath = string.Join(binding.HierarchyDelimiter, levels[..depth]);
            var ancestorName = NameFor(levels[depth - 1], fallback: binding.Alias);
            var declaredAncestor = aliasesByRemotePath.TryGetValue(ancestorPath, out var ancestorAlias)
                && ancestorAlias != binding.Alias
                    ? liveFolders.FirstOrDefault(row => row.Alias == ancestorAlias)
                    : null;
            var ancestor = declaredAncestor
                ?? FindUnnamedSibling(liveFolders, parentId, ancestorName)
                ?? Added(liveFolders, binding.MailboxAccountId, parentId, ancestorName, mintFolderId);

            parentId = ancestor.Id;
        }

        return parentId;
    }

    /// <summary>Names a folder after a remote level, falling back to another text when the level is no folder name.</summary>
    /// <remarks>
    /// A remote level can carry what a folder name may not — a format character, the local delimiter, an empty level —
    /// so those characters are dropped or replaced rather than the folder going unprojected.
    /// </remarks>
    private static string NameFor(string remoteLevel, string fallback) =>
        Sanitized(remoteLevel) ?? Sanitized(fallback) ?? LocalMailFolderName.Inbox.Value;

    private static string? Sanitized(string text)
    {
        var kept = new string([.. text
            .Where(static character => !char.IsControl(character)
                && char.GetUnicodeCategory(character) != UnicodeCategory.Format)
            .Select(static character => character == LocalMailFolderName.HierarchyDelimiter ? '-' : character)]).Trim();
        var bounded = kept.Length > LocalMailFolderName.MaximumLength ? kept[..LocalMailFolderName.MaximumLength].Trim() : kept;

        return bounded.Length == 0 ? null : bounded;
    }

    private static MailFolderEntity? FindUnnamedSibling(List<MailFolderEntity> liveFolders, Guid? parentId, string name)
    {
        var key = KeyOf(name);

        return liveFolders.FirstOrDefault(row => row.Alias is null && row.ParentId == parentId && row.NameKey == key);
    }

    private static MailFolderEntity Added(
        List<MailFolderEntity> liveFolders,
        string accountId,
        Guid? parentId,
        string name,
        Func<Guid> mintFolderId)
    {
        var folder = new MailFolderEntity
        {
            Id = mintFolderId(),
            MailboxAccountId = accountId,
            ParentId = parentId,
            Name = name,
            NameKey = KeyOf(name),
        };

        liveFolders.Add(folder);

        return folder;
    }

    /// <summary>Gets whether hanging a folder under a parent would make it its own ancestor.</summary>
    /// <remarks>
    /// Two bindings projected on different passes can describe the hierarchy inconsistently while a server reorganizes
    /// it; the folder then stays at the top until a later pass sees the settled shape, rather than forming a loop.
    /// </remarks>
    private static bool WouldLoop(List<MailFolderEntity> liveFolders, Guid folderId, Guid? parentId)
    {
        var visited = new HashSet<Guid>();

        for (var current = parentId; current is { } id && visited.Add(id);)
        {
            if (id == folderId)
            {
                return true;
            }

            current = liveFolders.FirstOrDefault(row => row.Id == id)?.ParentId;
        }

        return false;
    }

    /// <summary>Names the folder after its source folder, set apart by its alias when a sibling already carries that name.</summary>
    private static void Rename(MailFolderEntity folder, List<MailFolderEntity> liveFolders, string name, string alias)
    {
        var taken = liveFolders.Any(row => row.Id != folder.Id && row.ParentId == folder.ParentId && row.NameKey == KeyOf(name));
        var tag = Sanitized(alias) ?? string.Empty;
        var distinctName = taken
            ? $"{name[..Math.Min(name.Length, LocalMailFolderName.MaximumLength - tag.Length - 3)].Trim()} ({tag})"
            : name;

        folder.Name = distinctName;
        folder.NameKey = KeyOf(distinctName);
    }

    /// <summary>Gives the folder the role configuration declares for its alias, taking it from a folder no alias names.</summary>
    /// <remarks>
    /// A folder no alias names cannot have been given its role by configuration, so on a mirrored account it is a
    /// leftover — of a held hierarchy the account came back from, say — and configuration wins. A folder another alias
    /// names keeps a role it already plays, so two aliases declared with one role settle on the first rather than
    /// trading it on every pass.
    /// </remarks>
    private static void TakeRole(MailFolderEntity folder, List<MailFolderEntity> liveFolders, MailFolderSpecialUse? role)
    {
        if (role is not { } declared)
        {
            folder.Role = null;

            return;
        }

        var holder = liveFolders.FirstOrDefault(row => row.Id != folder.Id && row.Role == declared);

        if (holder is { Alias: not null })
        {
            folder.Role = null;

            return;
        }

        if (holder is not null)
        {
            holder.Role = null;
        }

        folder.Role = declared;
    }

    private static string KeyOf(string name) => LocalMailFolderName.Create(name).ComparisonKey;
}
