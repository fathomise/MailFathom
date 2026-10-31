// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContactPhoto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PhotoFileId",
                table: "contacts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_contacts_photo_file_id",
                table: "contacts",
                column: "PhotoFileId",
                filter: "\"PhotoFileId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_contacts_photo_holder",
                table: "contacts",
                sql: "\"PhotoFileId\" IS NULL OR \"UserId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_contacts_stored_files_photo",
                table: "contacts",
                column: "PhotoFileId",
                principalTable: "stored_files",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_contacts_stored_files_photo",
                table: "contacts");

            migrationBuilder.DropIndex(
                name: "ix_contacts_photo_file_id",
                table: "contacts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_contacts_photo_holder",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "PhotoFileId",
                table: "contacts");
        }
    }
}
