// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Folders;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One folder of one mail account, whatever copy of its mailbox is the truth.</summary>
/// <remarks>
/// On a mirrored account the rows are a projection of the source hierarchy that synchronization maintains; on a held
/// account MailFathom owns them. Either way the identifier is minted once and survives every rename, move, drain, and
/// restore, which is why a stored message names its folder by it. See
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0039-one-folder-model-and-one-implementation-per-custody-phase.md">ADR 0039</see>.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailFolderEntity
{
    public Guid Id { get; set; }

    public required string MailboxAccountId { get; set; }

    public Guid? ParentId { get; set; }

    public required string Name { get; set; }

    /// <summary>Gets or sets the form sibling names are compared in, which the uniqueness among live siblings is written over.</summary>
    public required string NameKey { get; set; }

    public MailFolderSpecialUse? Role { get; set; }

    /// <summary>Gets or sets the alias configuration names this folder by, or <see langword="null" /> for a folder no declaration names.</summary>
    /// <remarks>
    /// The key a folder mapping, a rule, a spam destination, and a per-folder setting are written under, and the key a
    /// source binding is matched to its folder by. A folder created on a held account carries none.
    /// </remarks>
    public string? Alias { get; set; }

    /// <summary>Gets or sets when the folder was erased, or <see langword="null" /> while it is live.</summary>
    /// <remarks>
    /// An erased row stays while its mail is erased in passes, so a message is never left pointing at nothing. Once no
    /// mail remains, a row carrying no alias goes; one carrying an alias stays, renamed to that alias, because it is
    /// what says arrivals from that source go to the inbox rather than recreating the folder.
    /// </remarks>
    public DateTimeOffset? ErasedAt { get; set; }
}
