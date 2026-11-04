// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordFilingsIntoAnotherAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cross_account_filings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DestinationStoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationFolderPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SourceAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SourceStoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                    RemovalMutationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ErasesHeldSource = table.Column<bool>(type: "boolean", nullable: false),
                    Stage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StageChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cross_account_filings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cross_account_filings_outstanding",
                table: "cross_account_filings",
                columns: new[] { "DestinationAccountId", "RecordedAt", "Id" },
                filter: "\"Stage\" IN ('Recorded', 'AppendIssued')");

            migrationBuilder.CreateIndex(
                name: "ix_cross_account_filings_removal",
                table: "cross_account_filings",
                column: "RemovalMutationId",
                unique: true,
                filter: "\"RemovalMutationId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cross_account_filings");
        }
    }
}
