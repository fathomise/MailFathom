// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel;
using MailFathom.Application.Folders;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Domain.Folders;

namespace MailFathom.Mcp.Tools.Accounts;

/// <summary>Publishes one folder of an account the caller's user owns.</summary>
/// <remarks>
/// Every folder of the account is listed the same way whichever copy of the mailbox is the truth: a folder the source
/// server holds and synchronization mirrors, and a folder MailFathom keeps itself — every folder of a held account, and
/// one a person created — which carries no synchronization fields because nothing synchronizes it.
/// </remarks>
[Description("One folder of a mail account, with the identity a request names it by, its place in the account's hierarchy, the role it plays, and how current its local copy is.")]
internal sealed record ListedMailFolder
{
    /// <summary>Gets the folder's identity.</summary>
    [Description("The identity of the folder, which is what folderId on an email and the folders argument of a mailbox tool name it by. It stays the same when the folder is renamed or moved.")]
    public required string FolderId { get; init; }

    /// <summary>Gets the folder's names from the top of its account's hierarchy down to it.</summary>
    [Description("The folder's place in its account's hierarchy, as the folder names from the top down to it joined by '/', such as INBOX or Projects/2026. It is for reading and changes when the folder is renamed or moved.")]
    public required string FolderPath { get; init; }

    /// <summary>Gets the role the folder plays, or <see langword="null" /> for none.</summary>
    [Description("The role the folder plays for its account — Inbox, Archive, Drafts, Sent, Junk, Trash, All, Flagged, or Important — which role:<Role> names it by in the folders argument. Null for a folder that plays none.")]
    public string? Role { get; init; }

    /// <summary>Gets whether a source server's copy of the folder is synchronized into it.</summary>
    [Description("Whether the folder is the local copy of a folder on the mail server that synchronization keeps current. False for a folder MailFathom keeps itself, whose mail is all here and never out of date.")]
    public required bool IsSynchronized { get; init; }

    /// <summary>Gets when progress was last committed for the folder, or <see langword="null" /> when it never was.</summary>
    [Description("When synchronization last durably committed progress for this folder, as an ISO 8601 timestamp. Null when isSynchronized is false, or when synchronization has never committed progress for it, which means its mail may be absent entirely rather than merely out of date.")]
    public DateTimeOffset? SynchronizedAt { get; init; }

    /// <summary>Publishes one folder.</summary>
    /// <param name="folder">The folder as local state holds it.</param>
    /// <param name="freshness">How current its synchronized copy is, or <see langword="null" /> for a folder nothing synchronizes.</param>
    /// <returns>The wire representation of <paramref name="folder" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="folder" /> is <see langword="null" />.</exception>
    public static ListedMailFolder From(StoredMailFolder folder, MailboxFolderFreshness? freshness)
    {
        ArgumentNullException.ThrowIfNull(folder);

        return new ListedMailFolder
        {
            FolderId = folder.Id.ToString(),
            FolderPath = StoredEmailFolder.PathOf(folder.HierarchyLevels),
            Role = folder.Role?.ToString(),
            IsSynchronized = freshness is not null,
            SynchronizedAt = freshness?.SynchronizedAt,
        };
    }
}
