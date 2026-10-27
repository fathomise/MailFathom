// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationMemberTrust : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SenderTrustOrganizationId",
                table: "stored_emails",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InternetMessageId",
                table: "outgoing_emails",
                type: "character varying(998)",
                maxLength: 998,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_sender_trust_organization",
                table: "stored_emails",
                column: "SenderTrustOrganizationId",
                filter: "\"SenderTrustOrganizationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_emails_internet_message_id",
                table: "outgoing_emails",
                columns: new[] { "InternetMessageId", "MailboxAccountId" },
                filter: "\"InternetMessageId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_stored_emails_sender_trust_organization",
                table: "stored_emails",
                column: "SenderTrustOrganizationId",
                principalTable: "organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_stored_emails_sender_trust_organization",
                table: "stored_emails");

            migrationBuilder.DropIndex(
                name: "ix_stored_emails_sender_trust_organization",
                table: "stored_emails");

            migrationBuilder.DropIndex(
                name: "ix_outgoing_emails_internet_message_id",
                table: "outgoing_emails");

            migrationBuilder.DropColumn(
                name: "SenderTrustOrganizationId",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "InternetMessageId",
                table: "outgoing_emails");
        }
    }
}
