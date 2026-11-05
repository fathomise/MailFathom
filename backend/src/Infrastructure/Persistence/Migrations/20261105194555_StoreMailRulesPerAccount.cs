// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StoreMailRulesPerAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "RuleName",
                table: "mail_rule_executions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AddColumn<Guid>(
                name: "RuleId",
                table: "mail_rule_executions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "mail_rule_sets",
                columns: table => new
                {
                    MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mail_rule_sets", x => x.MailboxAccountId);
                    table.ForeignKey(
                        name: "FK_mail_rule_sets_mailbox_accounts_MailboxAccountId",
                        column: x => x.MailboxAccountId,
                        principalTable: "mailbox_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mail_rule_set_revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Revision = table.Column<string>(type: "character(12)", fixedLength: true, maxLength: 12, nullable: false),
                    ReplacedRevision = table.Column<string>(type: "character(12)", fixedLength: true, maxLength: 12, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Surface = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CommittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Declarations = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mail_rule_set_revisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mail_rule_set_revisions_mail_rule_sets_MailboxAccountId",
                        column: x => x.MailboxAccountId,
                        principalTable: "mail_rule_sets",
                        principalColumn: "MailboxAccountId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mail_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Condition = table.Column<string>(type: "text", nullable: false),
                    Actions = table.Column<string>(type: "jsonb", nullable: false),
                    StopWhenMatched = table.Column<bool>(type: "boolean", nullable: false),
                    Triggers = table.Column<string[]>(type: "text[]", nullable: false),
                    Schedule = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mail_rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mail_rules_mail_rule_sets_MailboxAccountId",
                        column: x => x.MailboxAccountId,
                        principalTable: "mail_rule_sets",
                        principalColumn: "MailboxAccountId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mail_rule_set_revisions_account_revision_committed",
                table: "mail_rule_set_revisions",
                columns: new[] { "MailboxAccountId", "Revision", "CommittedAt" });

            migrationBuilder.CreateIndex(
                name: "ix_mail_rule_set_revisions_account_version",
                table: "mail_rule_set_revisions",
                columns: new[] { "MailboxAccountId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mail_rules_account_normalized_name",
                table: "mail_rules",
                columns: new[] { "MailboxAccountId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mail_rules_account_position",
                table: "mail_rules",
                columns: new[] { "MailboxAccountId", "Position" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mail_rule_set_revisions");

            migrationBuilder.DropTable(
                name: "mail_rules");

            migrationBuilder.DropTable(
                name: "mail_rule_sets");

            migrationBuilder.DropColumn(
                name: "RuleId",
                table: "mail_rule_executions");

            migrationBuilder.AlterColumn<string>(
                name: "RuleName",
                table: "mail_rule_executions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);
        }
    }
}
