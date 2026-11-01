// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>The footer template one organization's members send with from its accounts.</summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class OrganizationFooterTemplateEntity
{
    /// <summary>The table these rows live in, named here because the composed statements write it.</summary>
    internal const string TableName = "organization_footer_templates";

    /// <summary>The organization, which is the key and the foreign key at once.</summary>
    public Guid OrganizationId { get; set; }

    /// <summary>The template's sanitized markup and its images, as one <c>jsonb</c> document.</summary>
    public required string Document { get; set; }

    /// <summary>When the template was last written.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
