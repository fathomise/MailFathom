// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomaticReplies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "automatic_replies",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    InsideText = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    OutsideText = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    AccountIds = table.Column<string[]>(type: "text[]", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsOn = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automatic_replies", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_automatic_replies_settings_accounts_UserId",
                        column: x => x.UserId,
                        principalTable: "settings_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "automatic_reply_answers",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    AnsweredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automatic_reply_answers", x => new { x.UserId, x.Revision, x.SenderAddress });
                    table.ForeignKey(
                        name: "FK_automatic_reply_answers_automatic_replies_UserId",
                        column: x => x.UserId,
                        principalTable: "automatic_replies",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "automatic_reply_answers");

            migrationBuilder.DropTable(
                name: "automatic_replies");
        }
    }
}
