// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordJunkMarkChangesAndJunkFolderDepartures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "JunkMarksChangedAt",
                table: "stored_emails",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "email_junk_folder_departures",
                columns: table => new
                {
                    MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MessageIdDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DepartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_junk_folder_departures", x => new { x.MailboxAccountId, x.MessageIdDigest });
                    table.ForeignKey(
                        name: "FK_email_junk_folder_departures_mailbox_accounts_MailboxAccoun~",
                        column: x => x.MailboxAccountId,
                        principalTable: "mailbox_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_junk_marks_changed",
                table: "stored_emails",
                columns: new[] { "MailboxAccountId", "Id" },
                filter: "\"JunkMarksChangedAt\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_junk_folder_departures");

            migrationBuilder.DropIndex(
                name: "ix_stored_emails_junk_marks_changed",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "JunkMarksChangedAt",
                table: "stored_emails");
        }
    }
}
