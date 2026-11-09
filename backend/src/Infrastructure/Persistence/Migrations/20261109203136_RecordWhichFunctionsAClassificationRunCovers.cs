// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordWhichFunctionsAClassificationRunCovers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A table of its own rather than columns on the old one: an older build still running during a rolling upgrade
            // carries any row of spam_classification_runs with no end recorded, so it would walk the spam half of a run that
            // also asks for categories and finish it as if they were done. It cannot read a table it does not know.
            //
            // The old table stays, emptied, because that build still reads it; dropping it is a later change's, once no
            // release can be upgraded from with a replica still reading it. Emptying it hands an outstanding run to one
            // walker rather than two.
            migrationBuilder.CreateTable(
                name: "classification_runs",
                columns: table => new
                {
                    MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SpamFolderAliases = table.Column<string[]>(type: "text[]", nullable: false),
                    CategoryFolderAliases = table.Column<string[]>(type: "text[]", nullable: false),
                    OwnCategory = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Posture = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Rescores = table.Column<bool>(type: "boolean", nullable: false),
                    Profile = table.Column<string>(type: "character(12)", fixedLength: true, maxLength: 12, nullable: true),
                    Position = table.Column<Guid>(type: "uuid", nullable: true),
                    ClassifiedEmailCount = table.Column<int>(type: "integer", nullable: false),
                    SpamEmailCount = table.Column<int>(type: "integer", nullable: false),
                    UndeterminedEmailCount = table.Column<int>(type: "integer", nullable: false),
                    SkippedEmailCount = table.Column<int>(type: "integer", nullable: false),
                    UnclassifiableEmailCount = table.Column<int>(type: "integer", nullable: false),
                    ActedEmailCount = table.Column<int>(type: "integer", nullable: false),
                    CategorizedEmailCount = table.Column<int>(type: "integer", nullable: false),
                    CategoriesCurrentEmailCount = table.Column<int>(type: "integer", nullable: false),
                    CategoriesUnansweredEmailCount = table.Column<int>(type: "integer", nullable: false),
                    CategoriesClearedEmailCount = table.Column<int>(type: "integer", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Ending = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_classification_runs", x => x.MailboxAccountId);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO classification_runs (
                    "MailboxAccountId", "RequestedAt", "SpamFolderAliases", "CategoryFolderAliases", "Posture", "Rescores",
                    "Profile", "Position", "ClassifiedEmailCount", "SpamEmailCount", "UndeterminedEmailCount",
                    "SkippedEmailCount", "UnclassifiableEmailCount", "ActedEmailCount", "CategorizedEmailCount",
                    "CategoriesCurrentEmailCount", "CategoriesUnansweredEmailCount", "CategoriesClearedEmailCount",
                    "EndedAt", "Ending")
                SELECT
                    "MailboxAccountId", "RequestedAt", "FolderAliases", '{}', "Posture", "Rescores",
                    "Profile", "Position", "ClassifiedEmailCount", "SpamEmailCount", "UndeterminedEmailCount",
                    "SkippedEmailCount", "UnclassifiableEmailCount", "ActedEmailCount", 0,
                    0, 0, 0,
                    "EndedAt", "Ending"
                FROM spam_classification_runs;

                DELETE FROM spam_classification_runs;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "classification_runs");
        }
    }
}
