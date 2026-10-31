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

    /// <inheritdoc />
    public async Task<bool> SetJobTitleAsync(UserId user, string? jobTitle, CancellationToken cancellationToken)
    {
        var userId = RequireNamed(user);

        if (jobTitle is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(jobTitle);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(jobTitle.Length, UserRecord.MaximumJobTitleLength, nameof(jobTitle));
        }

        // The version is left where it is, for the reason the label's is: the title is not part of the document.
        var written = await dbContext.UserAccounts
            .Where(record => record.Id == userId)
            .ExecuteUpdateAsync(record => record.SetProperty(held => held.JobTitle, jobTitle), cancellationToken);

        return written > 0;
    }

    /// <inheritdoc />
    public async Task<bool> SetTypeAsync(UserId user, UserType type, CancellationToken cancellationToken)
    {
        var userId = RequireNamed(user);

        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), "A user's type names a declared value.");
        }

        var written = await dbContext.UserAccounts
            .Where(record => record.Id == userId)
            .ExecuteUpdateAsync(record => record.SetProperty(held => held.Type, type), cancellationToken);

        return written > 0;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Every write naming a manager in one organization takes that organization's row first, so two of them are read and
    /// judged one after the other: each sees the line the other committed, and two writes that would each close half a
    /// cycle cannot both pass. The manager's row is held shared from then on, which a move, a retyping, and an erasure
    /// of the manager each wait for, so the manager stays a person of the organization until the write commits.
    /// </para>
    /// <para>
    /// The user's own row is not held, because a move of the user takes it before it takes anything else and holding
    /// it here as well would order the two writes' locks against each other. The update states the organization it was
    /// judged against instead, so a user who left it in the meantime is written nothing and answered as outside it.
    /// </para>
    /// </remarks>
    public async Task<ManagerAssignmentResult> SetManagerAsync(
        UserId user,
        UserId? manager,
        CancellationToken cancellationToken)
    {
        var userId = RequireNamed(user);

        if (manager is not { } named)
        {
            var cleared = await dbContext.UserAccounts
                .Where(record => record.Id == userId)
                .ExecuteUpdateAsync(record => record.SetProperty(held => held.ManagerId, (Guid?)null), cancellationToken);

            return cleared > 0 ? ManagerAssignmentResult.Assigned : ManagerAssignmentResult.UnknownUser;
        }

        var managerId = RequireNamed(named);

        if (managerId == userId)
        {
            return ManagerAssignmentResult.OwnManager;
        }

        await using var assignment = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var organizationOfUser = await dbContext.UserAccounts
            .Where(record => record.Id == userId)
            .Select(record => new { record.OrganizationId })
            .SingleOrDefaultAsync(cancellationToken);

        if (organizationOfUser is null)
        {
            return ManagerAssignmentResult.UnknownUser;
        }

        if (organizationOfUser.OrganizationId is not { } organizationId)
        {
            return ManagerAssignmentResult.UserInNoOrganization;
        }

        await dbContext.Database.ExecuteSqlAsync(
            $"""SELECT 1 FROM organizations WHERE "Id" = {organizationId} FOR NO KEY UPDATE""",
            cancellationToken);
        await dbContext.Database.ExecuteSqlAsync(
            $"""SELECT 1 FROM settings_accounts WHERE "Id" = {managerId} FOR SHARE""",
            cancellationToken);

        var heldManager = await dbContext.UserAccounts
            .Where(record => record.Id == managerId)
            .Select(record => new { record.OrganizationId, record.Type })
            .SingleOrDefaultAsync(cancellationToken);

        if (heldManager is null)
        {
            return ManagerAssignmentResult.UnknownManager;
        }

        if (heldManager.OrganizationId != organizationId)
        {
            return ManagerAssignmentResult.ManagerOutsideOrganization;
        }

        if (heldManager.Type != UserType.User)
        {
            return ManagerAssignmentResult.ManagerNotAPerson;
        }

        var managers = await dbContext.UserAccounts
            .Where(record => record.OrganizationId == organizationId && record.ManagerId != null)
            .Select(record => new { record.Id, ManagerId = record.ManagerId!.Value })
            .ToDictionaryAsync(record => UserId.Create(record.Id), record => UserId.Create(record.ManagerId), cancellationToken);

        if (ReportingLine.WouldCycle(user, named, managers))
        {
            return ManagerAssignmentResult.ReportingCycle;
        }

        var written = await dbContext.UserAccounts
            .Where(record => record.Id == userId && record.OrganizationId == organizationId)
            .ExecuteUpdateAsync(record => record.SetProperty(held => held.ManagerId, managerId), cancellationToken);

        await assignment.CommitAsync(cancellationToken);

        return written > 0 ? ManagerAssignmentResult.Assigned : ManagerAssignmentResult.ManagerOutsideOrganization;
    }

    private static Guid RequireNamed(UserId user) =>
        user.IsSpecified
            ? user.Value
            : throw new ArgumentException("A user record is provisioned for a named user.", nameof(user));
}
