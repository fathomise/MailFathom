// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryAndReadReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ReadReceiptRequestNamesAnotherAddress",
                table: "stored_emails",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ReadReceiptRequestedTo",
                table: "stored_emails",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReadReceiptSentAt",
                table: "stored_emails",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportKind",
                table: "stored_emails",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int[]>(
                name: "ReportOutcomes",
                table: "stored_emails",
                type: "integer[]",
                nullable: false,
                defaultValue: new int[0]);

            migrationBuilder.AddColumn<Guid>(
                name: "ReportOutgoingEmailId",
                table: "stored_emails",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportOwnMessageSubject",
                table: "stored_emails",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "ReportRecipients",
                table: "stored_emails",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.CreateTable(
                name: "outgoing_email_receipt_outcomes",
                columns: table => new
                {
                    OutgoingEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientOrdinal = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ReportingServer = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outgoing_email_receipt_outcomes", x => new { x.OutgoingEmailId, x.RecipientOrdinal, x.Kind });
                    table.ForeignKey(
                        name: "fk_outgoing_email_receipt_outcomes_recipients",
                        columns: x => new { x.OutgoingEmailId, x.RecipientOrdinal },
                        principalTable: "outgoing_email_recipients",
                        principalColumns: new[] { "OutgoingEmailId", "Ordinal" },
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outgoing_email_receipt_outcomes");

            migrationBuilder.DropColumn(
                name: "ReadReceiptRequestNamesAnotherAddress",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "ReadReceiptRequestedTo",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "ReadReceiptSentAt",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "ReportKind",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "ReportOutcomes",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "ReportOutgoingEmailId",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "ReportOwnMessageSubject",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "ReportRecipients",
                table: "stored_emails");
        }
    }
}
