// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MailFathom.Infrastructure.Persistence.Footers;

/// <summary>Composes the two statements a footer and a template are written by.</summary>
/// <remarks>
/// <para>
/// Each is one statement that inserts or replaces the row, so two writes of one footer at once both land and the later
/// one stands, with no read in between for either to lose.
/// </para>
/// <para>
/// Each inserts only beside the row it hangs off — the assignment, or the organization — so a footer for an account
/// the user is not assigned, or a template for an organization nobody holds, writes nothing and says so by the count
/// rather than by a foreign key violation.
/// </para>
/// </remarks>
internal static class MailFooterStatements
{
    /// <summary>Composes the statement writing a user's own footer.</summary>
    /// <param name="model">The model the schema is generated from.</param>
    /// <returns>The statement, carrying <c>{0}</c> for the user, <c>{1}</c> for the account, <c>{2}</c> for the document, and <c>{3}</c> for the instant.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="model" /> is <see langword="null" />.</exception>
    public static string ComposeOwnUpsert(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var footers = PersistedSchemaNames.EntityTypeOf<MailAccountFooterEntity>(model);
        var assignments = PersistedSchemaNames.EntityTypeOf<MailAccountAssignmentEntity>(model);

        var userColumn = PersistedSchemaNames.QuotedColumn(footers, nameof(MailAccountFooterEntity.UserId));
        var accountColumn = PersistedSchemaNames.QuotedColumn(footers, nameof(MailAccountFooterEntity.MailAccountId));
        var documentColumn = PersistedSchemaNames.QuotedColumn(footers, nameof(MailAccountFooterEntity.Document));
        var updatedColumn = PersistedSchemaNames.QuotedColumn(footers, nameof(MailAccountFooterEntity.UpdatedAt));

        return $$"""
            INSERT INTO {{PersistedSchemaNames.QuotedTable(footers)}}
                ({{userColumn}}, {{accountColumn}}, {{documentColumn}}, {{updatedColumn}})
            SELECT {0}, {1}, {2}::jsonb, {3}
            FROM {{PersistedSchemaNames.QuotedTable(assignments)}}
            WHERE {{PersistedSchemaNames.QuotedColumn(assignments, nameof(MailAccountAssignmentEntity.UserId))}} = {0}
                AND {{PersistedSchemaNames.QuotedColumn(assignments, nameof(MailAccountAssignmentEntity.MailAccountId))}} = {1}
            ON CONFLICT ({{userColumn}}, {{accountColumn}}) DO UPDATE SET
                {{documentColumn}} = EXCLUDED.{{documentColumn}},
                {{updatedColumn}} = EXCLUDED.{{updatedColumn}}
            """;
    }

    /// <summary>Composes the statement writing an organization's template.</summary>
    /// <param name="model">The model the schema is generated from.</param>
    /// <returns>The statement, carrying <c>{0}</c> for the organization, <c>{1}</c> for the document, and <c>{2}</c> for the instant.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="model" /> is <see langword="null" />.</exception>
    public static string ComposeTemplateUpsert(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var templates = PersistedSchemaNames.EntityTypeOf<OrganizationFooterTemplateEntity>(model);
        var organizations = PersistedSchemaNames.EntityTypeOf<OrganizationEntity>(model);

        var organizationColumn = PersistedSchemaNames.QuotedColumn(templates, nameof(OrganizationFooterTemplateEntity.OrganizationId));
        var documentColumn = PersistedSchemaNames.QuotedColumn(templates, nameof(OrganizationFooterTemplateEntity.Document));
        var updatedColumn = PersistedSchemaNames.QuotedColumn(templates, nameof(OrganizationFooterTemplateEntity.UpdatedAt));

        return $$"""
            INSERT INTO {{PersistedSchemaNames.QuotedTable(templates)}}
                ({{organizationColumn}}, {{documentColumn}}, {{updatedColumn}})
            SELECT {0}, {1}::jsonb, {2}
            FROM {{PersistedSchemaNames.QuotedTable(organizations)}}
            WHERE {{PersistedSchemaNames.QuotedColumn(organizations, nameof(OrganizationEntity.Id))}} = {0}
            ON CONFLICT ({{organizationColumn}}) DO UPDATE SET
                {{documentColumn}} = EXCLUDED.{{documentColumn}},
                {{updatedColumn}} = EXCLUDED.{{updatedColumn}}
            """;
    }
}
