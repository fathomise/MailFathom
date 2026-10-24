// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One resolution of an alias to a folder on the source server: an attribute of a folder rather than the folder itself.</summary>
/// <remarks>
/// Every generation of an alias is kept, so an occurrence stays attributable to the remote folder it was read from. The
/// folder the binding binds is <see cref="FolderId" />, which every generation of one alias shares. See
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0039-one-folder-model-and-one-implementation-per-custody-phase.md">ADR 0039</see>.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailFolderBindingEntity
{
    public long Id { get; set; }

    public required string MailboxAccountId { get; set; }

    public required string Alias { get; set; }

    public int ResolutionGeneration { get; set; }

    public required string RemotePath { get; set; }

    // Stored as text rather than as a single character, because PostgreSQL pads `character(1)` and the provider
    // mapping of a nullable CLR `char` has not been validated against a real database yet.
    public string? HierarchyDelimiter { get; set; }

    /// <summary>Gets or sets the folder this binding binds, or <see langword="null" /> where no folder has been given it yet.</summary>
    /// <remarks>
    /// Absent only on a binding a held account resolved before any arrival placed its mail, and on one an older build
    /// wrote during a rolling upgrade before the database carried the reference onto it.
    /// </remarks>
    public Guid? FolderId { get; set; }

    public MailFolderEntity? Folder { get; set; }

    public required MailboxAccountEntity MailboxAccount { get; set; }

    public ICollection<StoredEmailEntity> StoredEmails { get; } = [];

    public SynchronizationCheckpointEntity? SynchronizationCheckpoint { get; set; }
}
