// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecognizeSendersByContactBooksWhenRead : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_stored_emails_sender_trust_address",
                table: "stored_emails");

            migrationBuilder.DropIndex(
                name: "ix_stored_emails_sender_trust_contact",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SenderTrustContactId",
                table: "stored_emails");

            migrationBuilder.AddColumn<bool>(
                name: "SenderTrustIsStrict",
                table: "stored_emails",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Whether the account was strict was never stored, so a row from before is read as strict and draws no Known
            // through a contact until a rederive records the real answer; only a row already Known was certainly relaxed.
            migrationBuilder.Sql(
                """
                UPDATE stored_emails
                SET "SenderTrustIsStrict" = "SenderTrustLevel" <> 'Known';
                """);

            // A contact is now found when the row is read, so a verdict a contact reached stores nothing of it. A personal
            // contact ranks last, so configuration had answered nothing under the revision the row keeps. An organization
            // contact ranks above the owner's own address and domain, which were never asked, so that row becomes the
            // not-evaluated verdict a rederive judges again. Not reversed by Down: the contact is found on every read.
            migrationBuilder.Sql(
                """
                UPDATE stored_emails
                SET "SenderTrustLevel" = 'Unknown',
                    "SenderTrustGrantedBy" = 'None',
                    "SenderTrustAuthorEvidence" = 'None',
                    "SenderTrustPolicyRevision" = CASE
                        WHEN "SenderTrustGrantedBy" = 'OrganizationContact' THEN NULL
                        ELSE "SenderTrustPolicyRevision"
                    END
                WHERE "SenderTrustGrantedBy" IN ('OrganizationContact', 'PersonalContact');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SenderTrustIsStrict",
                table: "stored_emails");

            migrationBuilder.AddColumn<Guid>(
                name: "SenderTrustContactId",
                table: "stored_emails",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_sender_trust_address",
                table: "stored_emails",
                columns: new[] { "MailboxAccountId", "SenderTrustAddress" },
                filter: "\"SenderTrustAddress\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_sender_trust_contact",
                table: "stored_emails",
                column: "SenderTrustContactId",
                filter: "\"SenderTrustContactId\" IS NOT NULL");
        }
    }
}
