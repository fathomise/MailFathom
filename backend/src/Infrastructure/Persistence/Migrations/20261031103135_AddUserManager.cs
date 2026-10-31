// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserManager : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ManagerId",
                table: "settings_accounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_settings_accounts_manager",
                table: "settings_accounts",
                column: "ManagerId");

            migrationBuilder.AddCheckConstraint(
                name: "ck_settings_accounts_not_own_manager",
                table: "settings_accounts",
                sql: "\"ManagerId\" <> \"Id\"");

            migrationBuilder.AddForeignKey(
                name: "fk_settings_accounts_manager",
                table: "settings_accounts",
                column: "ManagerId",
                principalTable: "settings_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_settings_accounts_manager",
                table: "settings_accounts");

            migrationBuilder.DropIndex(
                name: "ix_settings_accounts_manager",
                table: "settings_accounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_settings_accounts_not_own_manager",
                table: "settings_accounts");

            migrationBuilder.DropColumn(
                name: "ManagerId",
                table: "settings_accounts");
        }
    }
}
