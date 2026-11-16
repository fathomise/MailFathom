// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One user being served one mail account.</summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class MailAccountAssignmentEntity
{
    internal const string TableName = "mail_account_assignments";

    public Guid UserId { get; set; }

    public Guid MailAccountId { get; set; }

    public DateTimeOffset AssignedAt { get; set; }

    /// <summary>Gets whether the account stands in no organization, which is what holds it to this one assignment.</summary>
    /// <remarks>
    /// A copy of the account's own <c>OrganizationId</c> being null, kept by two triggers rather than by any writer: one
    /// computes it on every insert and update of an assignment, and one rewrites it on every assignment of an account
    /// whose organization changes. So whatever wrote the row — this build, an older one, or a statement typed at the
    /// database — the index over it reads the account as it stands.
    /// </remarks>
    public bool AccountInNoOrganization { get; private set; }
}
