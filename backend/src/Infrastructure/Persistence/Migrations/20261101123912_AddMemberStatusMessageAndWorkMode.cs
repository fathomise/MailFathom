// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberStatusMessageAndWorkMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StatusMessage",
                table: "member_presence",
                type: "character varying(280)",
                maxLength: 280,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StatusMessageClears",
                table: "member_presence",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StatusMessageClearsAt",
                table: "member_presence",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkMode",
                table: "member_presence",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WorkModeEndsAt",
                table: "member_presence",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StatusMessage",
                table: "member_presence");

            migrationBuilder.DropColumn(
                name: "StatusMessageClears",
                table: "member_presence");

            migrationBuilder.DropColumn(
                name: "StatusMessageClearsAt",
                table: "member_presence");

            migrationBuilder.DropColumn(
                name: "WorkMode",
                table: "member_presence");

            migrationBuilder.DropColumn(
                name: "WorkModeEndsAt",
                table: "member_presence");
        }
    }
}
