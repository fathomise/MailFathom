// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>Writes the envelope of a user an administrator records, and nothing inside their document.</summary>
/// <remarks>
/// <para>
/// Both statements are single and conditional, which is what makes them safe against the race they actually meet: two
/// writers reaching one user at the same moment. An insert guarded by <c>ON CONFLICT</c> leaves the loser having written
/// nothing rather than raising, and an update selecting the user's own row writes nothing where another writer erased
/// them first.
/// </para>
/// <para>
/// The document is provisioned as the empty object and is never written here. What a user's record holds is written by
/// the record's own administrative write, which judges and versions it, rather than by the act that only names them.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class PersistedUserProvisioning(MailFathomDbContext dbContext, TimeProvider timeProvider)
    : IUserProvisioning
{
    /// <summary>The document a user's row is provisioned with, which is the empty record rather than their declaration.</summary>
    private const string EmptyDocument = "{}";

    /// <inheritdoc />
    public async Task<UserProvisioningResult> ProvisionAsync(
        UserId user,
        string displayName,
        Guid? organizationId,
        CancellationToken cancellationToken)
    {
        var userId = RequireNamed(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("A user is recorded into a named organization or into none.", nameof(organizationId));
        }

        var provisionedAt = timeProvider.GetUtcNow();

        // The organization is selected rather than written as a value, so one whose deletion committed before this
        // statement began inserts nothing, and the read below turns that silence into an answer. A deletion still
        // uncommitted is one the selection cannot see: the insert then waits on the foreign key and is refused once the
        // deletion commits, which no conflict clause covers.
        try
        {
            await dbContext.Database.ExecuteSqlAsync(
                $"""
                 INSERT INTO settings_accounts ("Id", "DisplayName", "Document", "Version", "CreatedAt", "UpdatedAt", "OrganizationId")
                 SELECT {userId}, {displayName}, CAST({EmptyDocument} AS jsonb), 1, {provisionedAt}, {provisionedAt}, CAST({organizationId} AS uuid)
                 WHERE CAST({organizationId} AS uuid) IS NULL
                    OR EXISTS (SELECT 1 FROM organizations WHERE "Id" = CAST({organizationId} AS uuid))
                 ON CONFLICT DO NOTHING
                 """,
                cancellationToken);
        }
        catch (PostgresException violation) when (violation.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            return UserProvisioningResult.UnknownOrganization;
        }

        return await dbContext.UserAccounts.AsNoTracking().AnyAsync(record => record.Id == userId, cancellationToken)
            ? UserProvisioningResult.Provisioned
            : UserProvisioningResult.UnknownOrganization;
    }

    /// <inheritdoc />
    public async Task<bool> RelabelAsync(UserId user, string displayName, CancellationToken cancellationToken)
    {
        var userId = RequireNamed(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        // The version is deliberately left where it is — it guards the document a writer composes over, and the label
        // is not part of that document, so stepping it here would refuse a write that had read the record correctly.
        var relabelled = await dbContext.UserAccounts
            .Where(record => record.Id == userId)
            .ExecuteUpdateAsync(
                record => record.SetProperty(held => held.DisplayName, displayName),
                cancellationToken);

        return relabelled > 0;
    }

    private static Guid RequireNamed(UserId user) =>
        user.IsSpecified
            ? user.Value
            : throw new ArgumentException("A user record is provisioned for a named user.", nameof(user));
}
