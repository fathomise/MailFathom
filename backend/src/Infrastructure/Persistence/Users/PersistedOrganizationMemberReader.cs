// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Policies;
using Npgsql;
using NpgsqlTypes;

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>Reads an organization's people out of PostgreSQL, over one connection.</summary>
/// <remarks>
/// <para>
/// A bare command over the data source rather than a query through the model, for the reason
/// <see cref="PersistedUserSettingsDocumentReader" /> is one: each member's document is measured by PostgreSQL and
/// sent only where it is within what this build binds, which the model cannot express. A member whose document is past
/// that bound is still read, with no record, so a colleague is never missing from the directory because of what their
/// record holds.
/// </para>
/// <para>
/// The members are read by the organization index on the user's row, and their addresses by the assignments of exactly
/// those users. An account is a member's address only where it belongs to the same organization and nobody else is
/// assigned it, which the statement decides rather than the process, so a shared mailbox never leaves the database as
/// one person's address.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this reader.")]
[RequiresIntegrationCoverage]
internal sealed class PersistedOrganizationMemberReader(
    Func<NpgsqlDataSource> dataSource,
    DatabaseCommandTimeout commandTimeout)
    : IOrganizationMemberReader
{
    private const string SelectOrganization =
        """
        SELECT "DisplayName" FROM organizations WHERE "Id" = @organization;
        """;

    private const string SelectMembers =
        """
        SELECT
            member."Id",
            member."DisplayName",
            member."JobTitle",
            member."ManagerId",
            CASE WHEN octet_length(member."Document"::text) <= @maximumOctets THEN member."Document"::text END AS "Document"
        FROM settings_accounts AS member
        WHERE member."OrganizationId" = @organization
          AND member."Type" = @person
          AND (@member IS NULL OR member."Id" = @member)
        ORDER BY member."Id"
        LIMIT @limit;
        """;

    /// <summary>Reads the address of every account in the organization that one of the members, and nobody else, is assigned.</summary>
    private const string SelectOwnAddresses =
        """
        SELECT assignment."UserId", account."Id", account."EmailAddress"
        FROM mail_account_assignments AS assignment
        JOIN settings_mail_accounts AS account ON account."Id" = assignment."MailAccountId"
        WHERE assignment."UserId" = ANY(@members)
          AND account."OrganizationId" = @organization
          AND account."EmailAddress" IS NOT NULL
          AND NOT EXISTS (
              SELECT 1 FROM mail_account_assignments AS other
              WHERE other."MailAccountId" = assignment."MailAccountId" AND other."UserId" <> assignment."UserId");
        """;

    private int CommandTimeoutSeconds => (int)commandTimeout.Value.TotalSeconds;

    /// <inheritdoc />
    public Task<OrganizationMembers?> ReadAsync(Guid organization, int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return this.ReadAsync(organization, member: null, limit, cancellationToken);
    }

    /// <inheritdoc />
    public Task<OrganizationMembers?> ReadMemberAsync(Guid organization, UserId member, CancellationToken cancellationToken) =>
        this.ReadAsync(organization, member, limit: 1, cancellationToken);

    private async Task<OrganizationMembers?> ReadAsync(
        Guid organization,
        UserId? member,
        int limit,
        CancellationToken cancellationToken)
    {
        NpgsqlConnection connection;

        try
        {
            connection = await dataSource().OpenConnectionAsync(cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            throw new UserSettingsUnreadableException(
                UserSettingsReadFailures.DiagnoseWhileConnecting(exception),
                exception);
        }

        await using (connection)
        {
            try
            {
                if (await this.ReadOrganizationNameAsync(connection, organization, cancellationToken) is not { } name)
                {
                    return null;
                }

                var members = await this.ReadMembersAsync(connection, organization, member, limit, cancellationToken);
                var addresses = await this.ReadOwnAddressesAsync(
                    connection,
                    organization,
                    [.. members.Select(member => member.User.Value)],
                    cancellationToken);
                var policies = await GoverningSettingsPolicyStatements.ReadAsync(
                    connection,
                    transaction: null,
                    organization,
                    this.CommandTimeoutSeconds,
                    cancellationToken);

                return new OrganizationMembers(
                    name,
                    policies,
                    [
                        .. members.Select(member => member with
                        {
                            Addresses =
                            [
                                .. addresses
                                    .Where(address => address.User == member.User.Value)
                                    .OrderBy(address => address.Account.ToString("D"), StringComparer.Ordinal)
                                    .Select(address => address.EmailAddress),
                            ],
                        }),
                    ]);
            }
            catch (NpgsqlException exception)
            {
                throw new UserSettingsUnreadableException(
                    UserSettingsReadFailures.DiagnoseWhileReading(exception),
                    exception);
            }
        }
    }

    private async Task<string?> ReadOrganizationNameAsync(
        NpgsqlConnection connection,
        Guid organization,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(SelectOrganization, connection);

        command.CommandTimeout = this.CommandTimeoutSeconds;
        command.Parameters.AddWithValue("organization", organization);

        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private async Task<List<OrganizationMember>> ReadMembersAsync(
        NpgsqlConnection connection,
        Guid organization,
        UserId? member,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(SelectMembers, connection);

        command.CommandTimeout = this.CommandTimeoutSeconds;
        command.Parameters.AddWithValue("organization", organization);
        command.Parameters.Add(new NpgsqlParameter("member", NpgsqlDbType.Uuid) { Value = (object?)member?.Value ?? DBNull.Value });
        command.Parameters.AddWithValue("person", (int)UserType.User);
        command.Parameters.AddWithValue("maximumOctets", UserSettingsDocument.MaximumOctets);
        command.Parameters.AddWithValue("limit", limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var members = new List<OrganizationMember>();

        while (await reader.ReadAsync(cancellationToken))
        {
            members.Add(new OrganizationMember(
                UserId.Create(reader.GetGuid(0)),
                reader.GetString(1),
                await reader.IsDBNullAsync(2, cancellationToken) ? null : reader.GetString(2),
                await reader.IsDBNullAsync(4, cancellationToken) ? null : reader.GetString(4),
                [])
            {
                Manager = await reader.IsDBNullAsync(3, cancellationToken) ? null : UserId.Create(reader.GetGuid(3)),
            });
        }

        return members;
    }

    private async Task<List<(Guid User, Guid Account, string EmailAddress)>> ReadOwnAddressesAsync(
        NpgsqlConnection connection,
        Guid organization,
        Guid[] members,
        CancellationToken cancellationToken)
    {
        if (members.Length == 0)
        {
            return [];
        }

        await using var command = new NpgsqlCommand(SelectOwnAddresses, connection);

        command.CommandTimeout = this.CommandTimeoutSeconds;
        command.Parameters.AddWithValue("organization", organization);
        command.Parameters.AddWithValue("members", members);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var addresses = new List<(Guid User, Guid Account, string EmailAddress)>();

        while (await reader.ReadAsync(cancellationToken))
        {
            addresses.Add((reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2)));
        }

        return addresses;
    }
}
