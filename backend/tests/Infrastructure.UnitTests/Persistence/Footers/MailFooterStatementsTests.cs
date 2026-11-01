// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Footers;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Footers;

/// <summary>
/// A footer and a template are each written by one statement, and what each promises is in its text: that a second
/// save replaces the first rather than colliding on the key, and that a footer for an assignment or a template for an
/// organization that is gone affects no row instead of raising a foreign-key violation.
/// </summary>
public sealed class MailFooterStatementsTests
{
    [Fact]
    public void ComposeOwnUpsert_TheWrite_InsertsBesideTheAssignmentAndReplacesOnConflict()
    {
        // Act
        var statement = Composed(MailFooterStatements.ComposeOwnUpsert);

        // Assert
        Assert.Contains(
            "INSERT INTO \"mail_account_footers\"\n    (\"UserId\", \"MailAccountId\", \"Document\", \"UpdatedAt\")\nSELECT {0}, {1}, {2}::jsonb, {3}\nFROM \"mail_account_assignments\"\nWHERE \"UserId\" = {0}\n    AND \"MailAccountId\" = {1}",
            statement,
            StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (\"UserId\", \"MailAccountId\") DO UPDATE SET", statement, StringComparison.Ordinal);
        Assert.Contains("\"Document\" = EXCLUDED.\"Document\"", statement, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposeTemplateUpsert_TheWrite_InsertsBesideTheOrganizationAndReplacesOnConflict()
    {
        // Act
        var statement = Composed(MailFooterStatements.ComposeTemplateUpsert);

        // Assert
        Assert.Contains(
            "INSERT INTO \"organization_footer_templates\"\n    (\"OrganizationId\", \"Document\", \"UpdatedAt\")\nSELECT {0}, {1}::jsonb, {2}\nFROM \"organizations\"\nWHERE \"Id\" = {0}",
            statement,
            StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (\"OrganizationId\") DO UPDATE SET", statement, StringComparison.Ordinal);
        Assert.Contains("\"UpdatedAt\" = EXCLUDED.\"UpdatedAt\"", statement, StringComparison.Ordinal);
    }

    private static string Composed(Func<IModel, string> compose)
    {
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        return compose(context.Model).ReplaceLineEndings("\n");
    }
}
