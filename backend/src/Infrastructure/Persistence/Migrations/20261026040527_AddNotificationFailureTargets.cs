// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationFailureTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TargetMailDraftId",
                table: "notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetMailboxMutationId",
                table: "notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetOutgoingEmailId",
                table: "notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_TargetMailboxMutationId",
                table: "notifications",
                column: "TargetMailboxMutationId");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_TargetMailDraftId",
                table: "notifications",
                column: "TargetMailDraftId");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_TargetOutgoingEmailId",
                table: "notifications",
                column: "TargetOutgoingEmailId");

            migrationBuilder.AddForeignKey(
                name: "FK_notifications_mail_drafts_TargetMailDraftId",
                table: "notifications",
                column: "TargetMailDraftId",
                principalTable: "mail_drafts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_notifications_mailbox_mutations_TargetMailboxMutationId",
                table: "notifications",
                column: "TargetMailboxMutationId",
                principalTable: "mailbox_mutations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_notifications_outgoing_emails_TargetOutgoingEmailId",
                table: "notifications",
                column: "TargetOutgoingEmailId",
                principalTable: "outgoing_emails",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_notifications_mail_drafts_TargetMailDraftId",
                table: "notifications");

            migrationBuilder.DropForeignKey(
                name: "FK_notifications_mailbox_mutations_TargetMailboxMutationId",
                table: "notifications");

            migrationBuilder.DropForeignKey(
                name: "FK_notifications_outgoing_emails_TargetOutgoingEmailId",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_notifications_TargetMailboxMutationId",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_notifications_TargetMailDraftId",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_notifications_TargetOutgoingEmailId",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "TargetMailDraftId",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "TargetMailboxMutationId",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "TargetOutgoingEmailId",
                table: "notifications");
        }
    }
}
