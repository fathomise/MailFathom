// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One user's own footer for one mail account they are assigned.</summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class MailAccountFooterEntity
{
    /// <summary>The table these rows live in, named here because the composed statements write it.</summary>
    internal const string TableName = "mail_account_footers";

    /// <summary>The user whose footer this is.</summary>
    public Guid UserId { get; set; }

    /// <summary>The account the footer is sent from.</summary>
    public Guid MailAccountId { get; set; }

    /// <summary>The footer's sanitized markup and its images, as one <c>jsonb</c> document.</summary>
    public required string Document { get; set; }

    /// <summary>When the footer was last written.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
