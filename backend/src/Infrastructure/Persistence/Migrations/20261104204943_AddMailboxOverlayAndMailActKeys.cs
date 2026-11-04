// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMailboxOverlayAndMailActKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LocalDeleteCommittedAt",
                table: "stored_emails",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LocalCopyStoredEmailId",
                table: "mailbox_mutations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ObservedFlaggedState",
                table: "mailbox_mutations",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ObservedFolderId",
                table: "mailbox_mutations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "ObservedKeywords",
                table: "mailbox_mutations",
                type: "text[]",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ObservedSeenState",
                table: "mailbox_mutations",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OverlaysStoredState",
                table: "mailbox_mutations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "mail_act_keys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    StoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Outcome = table.Column<string>(type: "jsonb", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mail_act_keys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mail_act_keys_settings_accounts_UserId",
                        column: x => x.UserId,
                        principalTable: "settings_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mail_act_keys_recorded",
                table: "mail_act_keys",
                columns: new[] { "RecordedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "ix_mail_act_keys_user_key_email",
                table: "mail_act_keys",
                columns: new[] { "UserId", "Key", "StoredEmailId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mail_act_keys");

            migrationBuilder.DropColumn(
                name: "LocalDeleteCommittedAt",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "LocalCopyStoredEmailId",
                table: "mailbox_mutations");

            migrationBuilder.DropColumn(
                name: "ObservedFlaggedState",
                table: "mailbox_mutations");

            migrationBuilder.DropColumn(
                name: "ObservedFolderId",
                table: "mailbox_mutations");

            migrationBuilder.DropColumn(
                name: "ObservedKeywords",
                table: "mailbox_mutations");

            migrationBuilder.DropColumn(
                name: "ObservedSeenState",
                table: "mailbox_mutations");

            migrationBuilder.DropColumn(
                name: "OverlaysStoredState",
                table: "mailbox_mutations");
        }
    }
}
