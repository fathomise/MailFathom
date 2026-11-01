// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordOrganizationReceiptsOnTheReceivedMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ObservedInStoredEmailId",
                table: "outgoing_email_receipt_outcomes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_outgoing_email_receipt_outcomes_ObservedInStoredEmailId",
                table: "outgoing_email_receipt_outcomes",
                column: "ObservedInStoredEmailId");

            migrationBuilder.AddForeignKey(
                name: "fk_outgoing_email_receipt_outcomes_stored_emails",
                table: "outgoing_email_receipt_outcomes",
                column: "ObservedInStoredEmailId",
                principalTable: "stored_emails",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_outgoing_email_receipt_outcomes_stored_emails",
                table: "outgoing_email_receipt_outcomes");

            migrationBuilder.DropIndex(
                name: "IX_outgoing_email_receipt_outcomes_ObservedInStoredEmailId",
                table: "outgoing_email_receipt_outcomes");

            migrationBuilder.DropColumn(
                name: "ObservedInStoredEmailId",
                table: "outgoing_email_receipt_outcomes");
        }
    }
}
