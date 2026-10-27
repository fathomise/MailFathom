// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStoredEmailSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ArrivalFolderId",
                table: "stored_emails",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SettledAt",
                table: "stored_emails",
                type: "timestamp with time zone",
                nullable: true);

            // Every row already stored was readable before this column existed, so it is stamped settled: an upgrade
            // must hide nothing a reader could already see. One statement at upgrade time, before the partial index
            // below is built over the result.
            migrationBuilder.Sql("UPDATE stored_emails SET \"SettledAt\" = \"StoredAt\" WHERE \"SettledAt\" IS NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_stored_emails_ArrivalFolderId",
                table: "stored_emails",
                column: "ArrivalFolderId");

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_settling",
                table: "stored_emails",
                columns: new[] { "MailboxAccountId", "Id" },
                filter: "\"SettledAt\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_stored_emails_local_mail_folders_ArrivalFolderId",
                table: "stored_emails",
                column: "ArrivalFolderId",
                principalTable: "local_mail_folders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_stored_emails_local_mail_folders_ArrivalFolderId",
                table: "stored_emails");

            migrationBuilder.DropIndex(
                name: "IX_stored_emails_ArrivalFolderId",
                table: "stored_emails");

            migrationBuilder.DropIndex(
                name: "ix_stored_emails_settling",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "ArrivalFolderId",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SettledAt",
                table: "stored_emails");
        }
    }
}
