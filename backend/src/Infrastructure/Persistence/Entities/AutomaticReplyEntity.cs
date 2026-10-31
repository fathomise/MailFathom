// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class AutomaticReplyEntity
{
    /// <summary>The table these rows live in, named here because the composed statements write it.</summary>
    internal const string TableName = "automatic_replies";

    /// <summary>The user whose reply this is, which is the key and the foreign key at once.</summary>
    public Guid UserId { get; set; }

    /// <summary>The identity of the save that wrote the row, which the answered senders are recorded under.</summary>
    public Guid Revision { get; set; }

    /// <summary>The text for senders inside the user's organization, or <see langword="null" /> where they get the other one.</summary>
    public string? InsideText { get; set; }

    /// <summary>The text for every other sender.</summary>
    public required string OutsideText { get; set; }

    /// <summary>The identifiers of the accounts the reply answers on.</summary>
    public required string[] AccountIds { get; set; }

    /// <summary>When answering starts.</summary>
    public DateTimeOffset StartsAt { get; set; }

    /// <summary>When answering stops.</summary>
    public DateTimeOffset EndsAt { get; set; }

    /// <summary>Whether the reply is switched on.</summary>
    public bool IsOn { get; set; }

    /// <summary>When the reply was last saved.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
