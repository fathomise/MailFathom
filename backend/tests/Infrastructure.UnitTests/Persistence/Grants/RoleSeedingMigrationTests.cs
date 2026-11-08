// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Grants;

/// <summary>Holds the seeded roles to the rule that they are written once and edited by no later release that was not decided to.</summary>
/// <remarks>
/// <para>
/// A seeded role is an ordinary row an operator may have renamed, edited, or deleted, and a release that published a
/// permission and wrote it into a seeded list would widen everybody holding that role on upgrade — the drift a list
/// written out by name exists to prevent. So no statement in the migration chain may touch a role or its list unless it
/// is one of the writers named below, with the decision that admits it.
/// </para>
/// <para>
/// The rule reads the operations of the whole chain in order rather than migration by migration, and recognizes a
/// writer by what its SQL does rather than by the class it was written in. A release folds its cycle's migrations into
/// one, so a class name is something the next release removes, while the statements it carried run unchanged.
/// </para>
/// </remarks>
public sealed class RoleSeedingMigrationTests
{
    private const string UserRoleId = "01a11deb-3808-7000-8000-000000000001";

    private const string OrganizationAdministratorRoleId = "01a11deb-3808-7000-8000-000000000002";

    private const string AdministratorRoleId = "01a11deb-3808-7000-8000-000000000003";

    private static readonly string[] SeededRoleIds = [UserRoleId, OrganizationAdministratorRoleId, AdministratorRoleId];

    /// <summary>Every statement allowed to write roles, each named with the decision that admits it and the number of statements it is.</summary>
    /// <remarks>
    /// <para>
    /// <c>Seeding</c> writes the built-in roles once, naming all three, as ordinary rows an operator may rename, edit,
    /// or delete.
    /// </para>
    /// <para>
    /// <c>Carried-over mail grants</c> carries what each credential granted onto its user, so it creates roles of its own
    /// and assigns them — and the seeded <c>Mail user</c> — to users who held no role before it. It names no seeded role
    /// and widens nobody who already held one, which is the drift this rule exists to refuse.
    /// </para>
    /// <para>
    /// <c>Administrator as a pattern</c> is the one deliberate edit of a seeded list: ADR 0012 decides that
    /// <c>Administrator</c> lists the pattern <c>*</c>, which reaches on the day it is written exactly the names it
    /// replaces, and it names that role alone.
    /// </para>
    /// <para>
    /// <c>Permissions split by domain</c> rewrites names rather than grants: the release that splits the user-facing
    /// permissions by domain carries every role's list — seeded or written by an operator, alike — to the names its
    /// holders already acted under, so a list gains entries and nobody gains an act. It singles out no seeded role.
    /// </para>
    /// <para>
    /// <c>Reduction to User and Administrator</c> is the second deliberate edit: ADR 0012 decides that a database is
    /// seeded with <c>User</c>, listing one pattern per user-facing domain, and <c>Administrator</c> alone, so it renames
    /// and rewrites the role seeded as <c>Mail user</c> and removes <c>Organization administrator</c>. It names those two
    /// rows and never <c>Administrator</c>, whose list is decided by the rewrite above alone.
    /// </para>
    /// </remarks>
    private static readonly AdmittedRoleWriter[] AdmittedRoleWriters =
    [
        new("Seeding", sql => NamesExactly(sql, SeededRoleIds), UpStatements: 1, DownStatements: 0),
        new(
            "Carried-over mail grants",
            sql => NamesExactly(sql, [])
                && (sql.Contains("carried_mail_roles", StringComparison.Ordinal)
                    || sql.Contains("'Carried-over mail grant %'", StringComparison.Ordinal)),
            UpStatements: 1,
            DownStatements: 1),
        new("Administrator as a pattern", sql => NamesExactly(sql, [AdministratorRoleId]), UpStatements: 1, DownStatements: 1),
        new(
            "Permissions split by domain",
            sql => NamesExactly(sql, []) && sql.Contains("'mailfathom.assistant.use'", StringComparison.Ordinal),
            UpStatements: 1,
            DownStatements: 1),
        new(
            "Reduction to User and Administrator",
            sql => NamesExactly(sql, [UserRoleId, OrganizationAdministratorRoleId]),
            UpStatements: 1,
            DownStatements: 1),
    ];

    [Fact]
    public void Migrations_EveryStatementWritingARole_IsOneOfTheAdmittedWriters()
    {
        // Arrange
        var upOperations = ReadChainOperations(migration => migration.UpOperations);

        // Act
        var unadmitted = upOperations
            .Where(WritesARole)
            .Where(operation => !AdmittedRoleWriters.Any(writer => writer.Recognizes(operation)))
            .Select(Describe)
            .ToArray();

        // Assert
        Assert.Empty(unadmitted);
    }

    /// <summary>
    /// Each admitted writer is recognized as exactly the statements it was decided as, in both directions, so a writer
    /// the chain no longer carries fails here instead of admitting nothing, and a later statement that happens to match a
    /// writer's shape fails as one statement too many instead of passing under that writer's decision.
    /// </summary>
    [Fact]
    public void Migrations_EachAdmittedWriter_IsExactlyTheStatementsItWasDecidedAs()
    {
        // Arrange
        var upOperations = ReadChainOperations(migration => migration.UpOperations);
        var downOperations = ReadChainOperations(migration => migration.DownOperations);

        // Act
        var recognized = AdmittedRoleWriters
            .Select(writer => (
                writer.Name,
                Up: upOperations.Count(writer.Recognizes),
                Down: downOperations.Count(writer.Recognizes)))
            .ToArray();

        // Assert
        Assert.Equal(
            AdmittedRoleWriters.Select(writer => (writer.Name, Up: writer.UpStatements, Down: writer.DownStatements)),
            recognized);
    }

    private static bool NamesExactly(string sql, string[] roleIds) =>
        SeededRoleIds.All(roleId => sql.Contains(roleId, StringComparison.Ordinal) == roleIds.Contains(roleId));

    private static bool WritesARole(MigrationOperation operation) => operation switch
    {
        SqlOperation sql => sql.Sql.Contains(RolePermissionEntity.TableName, StringComparison.Ordinal)
            || sql.Sql.Contains($"INTO {RoleEntity.TableName} ", StringComparison.Ordinal)
            || sql.Sql.Contains($"UPDATE {RoleEntity.TableName} ", StringComparison.Ordinal)
            || sql.Sql.Contains($"FROM {RoleEntity.TableName} ", StringComparison.Ordinal),
        InsertDataOperation insert => IsRoleTable(insert.Table),
        UpdateDataOperation update => IsRoleTable(update.Table),
        DeleteDataOperation delete => IsRoleTable(delete.Table),
        _ => false,
    };

    private static bool IsRoleTable(string table) =>
        table is RoleEntity.TableName or RolePermissionEntity.TableName;

    private static string Describe(MigrationOperation operation) => operation switch
    {
        SqlOperation sql => sql.Sql,
        _ => operation.GetType().Name,
    };

    /// <summary>The operations of every migration in the order a database applies them, in one direction.</summary>
    private static MigrationOperation[] ReadChainOperations(
        Func<Migration, IReadOnlyList<MigrationOperation>> direction)
    {
        using var context = CreateContext();
        var migrations = context.GetService<IMigrationsAssembly>();

        return
        [
            .. migrations.Migrations
                .OrderBy(migration => migration.Key, StringComparer.Ordinal)
                .SelectMany(migration => direction(
                    migrations.CreateMigration(migration.Value, context.Database.ProviderName!))),
        ];
    }

    private static MailFathomDbContext CreateContext() => new(
        MailFathomDbContextDesignTimeFactory.BuildOptions(
            orchestratedConnectionString: null,
            designTimeConnectionString: null),
        PostgresTextSearchConfiguration.Default);

    /// <summary>One statement the rule admits, recognized by what its SQL names.</summary>
    private sealed record AdmittedRoleWriter(
        string Name,
        Func<string, bool> RecognizesSql,
        int UpStatements,
        int DownStatements)
    {
        public bool Recognizes(MigrationOperation operation) =>
            operation is SqlOperation sql && WritesARole(sql) && this.RecognizesSql(sql.Sql);
    }
}
