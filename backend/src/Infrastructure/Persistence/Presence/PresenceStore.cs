// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access;
using MailFathom.Application.Access.Sessions;
using MailFathom.Application.Presence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Domain.Presence;
using MailFathom.Infrastructure.Persistence.Entities;
using Npgsql;
using NpgsqlTypes;

namespace MailFathom.Infrastructure.Persistence.Presence;

/// <summary>Holds members' open clients and their chosen presence in the two tables every replica reads.</summary>
/// <remarks>
/// <para>
/// Bare commands over the data source rather than EF Core queries, as the signal ticket store is written: a renewal is
/// an upsert bounded by a count, a sweep is a bounded delete, and none of it is a unit of work or a tracked entity.
/// Every identifier comes from the mapped entities' own constants and every value is a parameter.
/// </para>
/// <para>
/// Every instant is the caller's, read from the application's clock, so the expiry a row is written with and the
/// instant a read leaves it out at come from one source.
/// </para>
/// <para>
/// The pool arrives behind a delegate because the signal channel, which asks who belongs to an organization, is composed
/// before startup has composed the connection string the pool is built from.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this store.")]
[RequiresIntegrationCoverage]
internal sealed class PresenceStore(Func<NpgsqlDataSource> dataSource) : IPresenceStore
{
    private const string ReadClientsStatement = $"""
        SELECT "{PresenceClientEntity.UserIdColumnName}", "{PresenceClientEntity.ActivityColumnName}", "{PresenceClientEntity.ExpiresAtColumnName}"
        FROM "{PresenceClientEntity.TableName}"
        WHERE "{PresenceClientEntity.UserIdColumnName}" = ANY(@members);
        """;

    private const string ReadMembersStatement = $"""
        SELECT "{MemberPresenceEntity.UserIdColumnName}", "{MemberPresenceEntity.ChosenStateColumnName}", "{MemberPresenceEntity.ChoiceDurationColumnName}", "{MemberPresenceEntity.ChoiceEndsAtColumnName}", "{MemberPresenceEntity.LastSeenAtColumnName}",
            "{MemberPresenceEntity.StatusMessageColumnName}", "{MemberPresenceEntity.StatusMessageClearsColumnName}", "{MemberPresenceEntity.StatusMessageClearsAtColumnName}",
            "{MemberPresenceEntity.WorkModeColumnName}", "{MemberPresenceEntity.WorkModeEndsAtColumnName}"
        FROM "{MemberPresenceEntity.TableName}"
        WHERE "{MemberPresenceEntity.UserIdColumnName}" = ANY(@members);
        """;

    /// <summary>Reads the phone sessions an opening was recorded on, which every read judges by their expiry and their day.</summary>
    private const string ReadPhoneAppSessionsStatement = $"""
        SELECT "{ClientSessionEntity.UserIdColumnName}", "{ClientSessionEntity.LastOpenedOnColumnName}", "{ClientSessionEntity.ExpiresAtColumnName}"
        FROM "{ClientSessionEntity.TableName}"
        WHERE "{ClientSessionEntity.UserIdColumnName}" = ANY(@members)
          AND "{ClientSessionEntity.ClientKindColumnName}" = @phone
          AND "{ClientSessionEntity.LastOpenedOnColumnName}" IS NOT NULL;
        """;

    private const string RemoveOwnExpiredStatement = $"""
        DELETE FROM "{PresenceClientEntity.TableName}"
        WHERE "{PresenceClientEntity.UserIdColumnName}" = @member AND "{PresenceClientEntity.ExpiresAtColumnName}" <= @now;
        """;

    /// <summary>Renews a client already held, or writes a new one while the member is under the bound.</summary>
    /// <remarks>
    /// The count and the write are one statement, so two replicas renewing the same member see the same rows. Two new
    /// clients written at the same instant can each find room under the bound and both be written: the bound is a
    /// ceiling on what one member costs, not a guarantee, and one row over it costs nothing.
    /// </remarks>
    private const string RenewClientStatement = $"""
        INSERT INTO "{PresenceClientEntity.TableName}"
            ("{PresenceClientEntity.UserIdColumnName}", "{PresenceClientEntity.ClientIdColumnName}", "{PresenceClientEntity.ActivityColumnName}", "{PresenceClientEntity.ExpiresAtColumnName}")
        SELECT @member, @client, @activity, @expiresAt
        WHERE EXISTS (
                SELECT 1 FROM "{PresenceClientEntity.TableName}"
                WHERE "{PresenceClientEntity.UserIdColumnName}" = @member AND "{PresenceClientEntity.ClientIdColumnName}" = @client)
            OR (SELECT count(*) FROM "{PresenceClientEntity.TableName}" WHERE "{PresenceClientEntity.UserIdColumnName}" = @member) < @mostOpen
        ON CONFLICT ("{PresenceClientEntity.UserIdColumnName}", "{PresenceClientEntity.ClientIdColumnName}") DO UPDATE
        SET "{PresenceClientEntity.ActivityColumnName}" = EXCLUDED."{PresenceClientEntity.ActivityColumnName}",
            "{PresenceClientEntity.ExpiresAtColumnName}" = EXCLUDED."{PresenceClientEntity.ExpiresAtColumnName}";
        """;

    private const string RecordLastSeenStatement = $"""
        INSERT INTO "{MemberPresenceEntity.TableName}" ("{MemberPresenceEntity.UserIdColumnName}", "{MemberPresenceEntity.LastSeenAtColumnName}")
        VALUES (@member, @lastSeen)
        ON CONFLICT ("{MemberPresenceEntity.UserIdColumnName}") DO UPDATE
        SET "{MemberPresenceEntity.LastSeenAtColumnName}" = EXCLUDED."{MemberPresenceEntity.LastSeenAtColumnName}";
        """;

    private const string RemoveClientStatement = $"""
        DELETE FROM "{PresenceClientEntity.TableName}"
        WHERE "{PresenceClientEntity.UserIdColumnName}" = @member AND "{PresenceClientEntity.ClientIdColumnName}" = @client;
        """;

    private const string ChooseStatement = $"""
        INSERT INTO "{MemberPresenceEntity.TableName}"
            ("{MemberPresenceEntity.UserIdColumnName}", "{MemberPresenceEntity.ChosenStateColumnName}", "{MemberPresenceEntity.ChoiceDurationColumnName}", "{MemberPresenceEntity.ChoiceEndsAtColumnName}")
        VALUES (@member, @chosenState, @choiceDuration, @choiceEndsAt)
        ON CONFLICT ("{MemberPresenceEntity.UserIdColumnName}") DO UPDATE
        SET "{MemberPresenceEntity.ChosenStateColumnName}" = EXCLUDED."{MemberPresenceEntity.ChosenStateColumnName}",
            "{MemberPresenceEntity.ChoiceDurationColumnName}" = EXCLUDED."{MemberPresenceEntity.ChoiceDurationColumnName}",
            "{MemberPresenceEntity.ChoiceEndsAtColumnName}" = EXCLUDED."{MemberPresenceEntity.ChoiceEndsAtColumnName}";
        """;

    private const string WriteStatusMessageStatement = $"""
        INSERT INTO "{MemberPresenceEntity.TableName}"
            ("{MemberPresenceEntity.UserIdColumnName}", "{MemberPresenceEntity.StatusMessageColumnName}", "{MemberPresenceEntity.StatusMessageClearsColumnName}", "{MemberPresenceEntity.StatusMessageClearsAtColumnName}")
        VALUES (@member, @text, @clears, @clearsAt)
        ON CONFLICT ("{MemberPresenceEntity.UserIdColumnName}") DO UPDATE
        SET "{MemberPresenceEntity.StatusMessageColumnName}" = EXCLUDED."{MemberPresenceEntity.StatusMessageColumnName}",
            "{MemberPresenceEntity.StatusMessageClearsColumnName}" = EXCLUDED."{MemberPresenceEntity.StatusMessageClearsColumnName}",
            "{MemberPresenceEntity.StatusMessageClearsAtColumnName}" = EXCLUDED."{MemberPresenceEntity.StatusMessageClearsAtColumnName}";
        """;

    private const string ChooseWorkModeStatement = $"""
        INSERT INTO "{MemberPresenceEntity.TableName}"
            ("{MemberPresenceEntity.UserIdColumnName}", "{MemberPresenceEntity.WorkModeColumnName}", "{MemberPresenceEntity.WorkModeEndsAtColumnName}")
        VALUES (@member, @workMode, @endsAt)
        ON CONFLICT ("{MemberPresenceEntity.UserIdColumnName}") DO UPDATE
        SET "{MemberPresenceEntity.WorkModeColumnName}" = EXCLUDED."{MemberPresenceEntity.WorkModeColumnName}",
            "{MemberPresenceEntity.WorkModeEndsAtColumnName}" = EXCLUDED."{MemberPresenceEntity.WorkModeEndsAtColumnName}";
        """;

    /// <summary>Removes a bounded batch of expired rows, reached through the index on the expiry.</summary>
    private const string RemoveExpiredStatement = $"""
        DELETE FROM "{PresenceClientEntity.TableName}"
        WHERE ctid IN (
            SELECT ctid FROM "{PresenceClientEntity.TableName}"
            WHERE "{PresenceClientEntity.ExpiresAtColumnName}" <= @removableFrom
            LIMIT @mostRemoved);
        """;

    private const string ReadOrganizationUsersStatement = $"""
        SELECT "{UserAccountEntity.IdColumnName}" FROM "{UserAccountEntity.TableName}"
        WHERE "{UserAccountEntity.OrganizationIdColumnName}" = @organization
          AND "{UserAccountEntity.TypeColumnName}" = @person
        ORDER BY "{UserAccountEntity.IdColumnName}"
        LIMIT @most;
        """;

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<UserId, MemberPresence>> ReadAsync(
        IReadOnlyCollection<UserId> members,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(members.Count, IPresenceStore.MostReadMembers);

        if (members.Count == 0)
        {
            return new Dictionary<UserId, MemberPresence>();
        }

        Guid[] identifiers = [.. members.Select(member => member.Value)];
        var clients = new Dictionary<Guid, List<OpenPresenceClient>>();
        var held = new Dictionary<Guid, HeldForMember>();
        var phoneAppSessions = new Dictionary<Guid, List<PhoneAppSession>>();

        await using var batch = dataSource().CreateBatch();
        batch.BatchCommands.Add(WithMembers(new NpgsqlBatchCommand(ReadClientsStatement), identifiers));
        batch.BatchCommands.Add(WithMembers(new NpgsqlBatchCommand(ReadMembersStatement), identifiers));

        var readPhoneAppSessions = WithMembers(new NpgsqlBatchCommand(ReadPhoneAppSessionsStatement), identifiers);
        readPhoneAppSessions.Parameters.AddWithValue("phone", (int)ClientKind.Android);
        batch.BatchCommands.Add(readPhoneAppSessions);

        await using (var reader = await batch.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var member = reader.GetGuid(0);
                var client = new OpenPresenceClient((ClientActivity)reader.GetInt32(1), reader.GetFieldValue<DateTimeOffset>(2));

                if (clients.TryGetValue(member, out var list))
                {
                    list.Add(client);
                }
                else
                {
                    clients[member] = [client];
                }
            }

            await reader.NextResultAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var choice = await reader.IsDBNullAsync(1, cancellationToken)
                    ? null
                    : new PresenceChoice(
                        (PresenceState)reader.GetInt32(1),
                        await reader.IsDBNullAsync(2, cancellationToken) ? null : (PresenceDuration)reader.GetInt32(2),
                        await reader.IsDBNullAsync(3, cancellationToken) ? null : reader.GetFieldValue<DateTimeOffset>(3));
                DateTimeOffset? lastSeen = await reader.IsDBNullAsync(4, cancellationToken)
                    ? null
                    : reader.GetFieldValue<DateTimeOffset>(4);
                var message = await reader.IsDBNullAsync(5, cancellationToken)
                    ? null
                    : new StatusMessage(
                        reader.GetString(5),
                        await reader.IsDBNullAsync(6, cancellationToken) ? null : (PresenceDuration)reader.GetInt32(6),
                        await reader.IsDBNullAsync(7, cancellationToken) ? null : reader.GetFieldValue<DateTimeOffset>(7));
                var workMode = await reader.IsDBNullAsync(8, cancellationToken)
                    ? null
                    : new WorkModeChoice(
                        (WorkMode)reader.GetInt32(8),
                        await reader.IsDBNullAsync(9, cancellationToken) ? null : reader.GetFieldValue<DateTimeOffset>(9));

                held[reader.GetGuid(0)] = new HeldForMember(choice, lastSeen, message, workMode);
            }

            await reader.NextResultAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var member = reader.GetGuid(0);
                var session = new PhoneAppSession(reader.GetFieldValue<DateOnly>(1), reader.GetFieldValue<DateTimeOffset>(2));

                if (phoneAppSessions.TryGetValue(member, out var list))
                {
                    list.Add(session);
                }
                else
                {
                    phoneAppSessions[member] = [session];
                }
            }
        }

        return members.Distinct().ToDictionary(
            member => member,
            member =>
            {
                var stated = held.GetValueOrDefault(member.Value);

                return new MemberPresence(
                    clients.TryGetValue(member.Value, out var open) ? open : [],
                    stated?.Choice,
                    stated?.LastSeen,
                    stated?.Message,
                    stated?.WorkMode)
                {
                    PhoneAppSessions = phoneAppSessions.TryGetValue(member.Value, out var phone) ? phone : [],
                };
            });
    }

    /// <inheritdoc />
    public async Task<bool> RenewClientAsync(
        UserId member,
        Guid client,
        ClientActivity activity,
        DateTimeOffset now,
        DateTimeOffset? lastSeen,
        CancellationToken cancellationToken)
    {
        await using var batch = dataSource().CreateBatch();

        var removal = new NpgsqlBatchCommand(RemoveOwnExpiredStatement);
        removal.Parameters.AddWithValue("member", member.Value);
        removal.Parameters.AddWithValue("now", now.ToUniversalTime());
        batch.BatchCommands.Add(removal);

        var renewal = new NpgsqlBatchCommand(RenewClientStatement);
        renewal.Parameters.AddWithValue("member", member.Value);
        renewal.Parameters.AddWithValue("client", client);
        renewal.Parameters.AddWithValue("activity", (int)activity);
        renewal.Parameters.AddWithValue("expiresAt", (now + MemberPresence.ClientLifetime).ToUniversalTime());
        renewal.Parameters.AddWithValue("mostOpen", (long)MemberPresence.MostOpenClients);
        batch.BatchCommands.Add(renewal);

        await batch.ExecuteNonQueryAsync(cancellationToken);

        if (renewal.RecordsAffected != 1)
        {
            return false;
        }

        if (lastSeen is { } seen)
        {
            await using var command = dataSource().CreateCommand(RecordLastSeenStatement);
            command.Parameters.AddWithValue("member", member.Value);
            command.Parameters.AddWithValue("lastSeen", seen.ToUniversalTime());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return true;
    }

    /// <inheritdoc />
    public async Task RemoveClientAsync(UserId member, Guid client, CancellationToken cancellationToken)
    {
        await using var command = dataSource().CreateCommand(RemoveClientStatement);
        command.Parameters.AddWithValue("member", member.Value);
        command.Parameters.AddWithValue("client", client);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task ChooseAsync(UserId member, PresenceChoice? choice, CancellationToken cancellationToken)
    {
        await using var command = dataSource().CreateCommand(ChooseStatement);
        command.Parameters.AddWithValue("member", member.Value);
        command.Parameters.Add(new NpgsqlParameter("chosenState", NpgsqlDbType.Integer)
        {
            Value = choice is null ? DBNull.Value : (int)choice.State,
        });
        command.Parameters.Add(new NpgsqlParameter("choiceDuration", NpgsqlDbType.Integer)
        {
            Value = choice?.Duration is { } duration ? (int)duration : DBNull.Value,
        });
        command.Parameters.Add(new NpgsqlParameter("choiceEndsAt", NpgsqlDbType.TimestampTz)
        {
            Value = choice?.EndsAt is { } end ? end.ToUniversalTime() : DBNull.Value,
        });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task WriteStatusMessageAsync(UserId member, StatusMessage? message, CancellationToken cancellationToken)
    {
        await using var command = dataSource().CreateCommand(WriteStatusMessageStatement);
        command.Parameters.AddWithValue("member", member.Value);
        command.Parameters.Add(new NpgsqlParameter("text", NpgsqlDbType.Varchar)
        {
            Value = message is null ? DBNull.Value : message.Text,
        });
        command.Parameters.Add(new NpgsqlParameter("clears", NpgsqlDbType.Integer)
        {
            Value = message?.Clears is { } clears ? (int)clears : DBNull.Value,
        });
        command.Parameters.Add(new NpgsqlParameter("clearsAt", NpgsqlDbType.TimestampTz)
        {
            Value = message?.ClearsAt is { } clearsAt ? clearsAt.ToUniversalTime() : DBNull.Value,
        });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task ChooseWorkModeAsync(UserId member, WorkModeChoice workMode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workMode);

        await using var command = dataSource().CreateCommand(ChooseWorkModeStatement);
        command.Parameters.AddWithValue("member", member.Value);
        command.Parameters.AddWithValue("workMode", (int)workMode.Mode);
        command.Parameters.Add(new NpgsqlParameter("endsAt", NpgsqlDbType.TimestampTz)
        {
            Value = workMode.EndsAt is { } end ? end.ToUniversalTime() : DBNull.Value,
        });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveExpiredClientsAsync(
        DateTimeOffset removableFrom,
        int mostRemoved,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource().CreateCommand(RemoveExpiredStatement);
        command.Parameters.AddWithValue("removableFrom", removableFrom.ToUniversalTime());
        command.Parameters.AddWithValue("mostRemoved", mostRemoved);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserId>> ReadOrganizationUsersAsync(Guid organization, CancellationToken cancellationToken)
    {
        await using var command = dataSource().CreateCommand(ReadOrganizationUsersStatement);
        command.Parameters.AddWithValue("organization", organization);
        command.Parameters.AddWithValue("person", (int)UserType.User);
        command.Parameters.AddWithValue("most", IPresenceStore.MostReadMembers);

        var users = new List<UserId>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            users.Add(UserId.Create(reader.GetGuid(0)));
        }

        return users;
    }

    private static NpgsqlBatchCommand WithMembers(NpgsqlBatchCommand command, Guid[] members)
    {
        command.Parameters.AddWithValue("members", members);

        return command;
    }

    private sealed record HeldForMember(
        PresenceChoice? Choice,
        DateTimeOffset? LastSeen,
        StatusMessage? Message,
        WorkModeChoice? WorkMode);
}
