// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One committed write to a mail account's rules, appended and never updated but for its actor's erasure.</summary>
/// <remarks>
/// The actor is a plain user identifier with no foreign key, because a row of a mailbox's record must not be deleted by
/// a cascade from a person: erasing the user clears the column and keeps the row.
/// </remarks>
internal sealed class MailRuleSetRevisionEntity
{
    /// <summary>The greatest length a stored surface name has.</summary>
    internal const int MaximumSurfaceLength = 64;

    /// <summary>Gets or sets the row's own identifier.</summary>
    public required Guid Id { get; set; }

    /// <summary>Gets or sets the account the rules belong to.</summary>
    public required string MailboxAccountId { get; set; }

    /// <summary>Gets or sets the version the write produced.</summary>
    public long Version { get; set; }

    /// <summary>Gets or sets the revision the write produced.</summary>
    public required string Revision { get; set; }

    /// <summary>Gets or sets the revision the set had before the write.</summary>
    public required string ReplacedRevision { get; set; }

    /// <summary>Gets or sets the user who wrote it, empty once that user is erased or where no user did.</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>Gets or sets the surface the write came through, by its name.</summary>
    public required string Surface { get; set; }

    /// <summary>Gets or sets when the write committed.</summary>
    public DateTimeOffset CommittedAt { get; set; }

    /// <summary>Gets or sets every rule of the set as it stood after the write, as a JSON array of declarations.</summary>
    public required string Declarations { get; set; }
}
