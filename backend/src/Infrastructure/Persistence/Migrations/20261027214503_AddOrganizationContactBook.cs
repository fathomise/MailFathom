// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationContactBook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_contacts_book_holder",
                table: "contacts");

            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationId",
                table: "contacts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_contacts_OrganizationId",
                table: "contacts",
                column: "OrganizationId");

            migrationBuilder.AddCheckConstraint(
                name: "ck_contacts_book_holder",
                table: "contacts",
                sql: "((\"UserId\" IS NOT NULL)::int + (\"OrganizationId\" IS NOT NULL)::int + (\"MailboxAccountId\" IS NOT NULL)::int) = 1\nAND \"BookHolderId\" = CASE WHEN \"UserId\" IS NOT NULL THEN 'user:' || \"UserId\"::text WHEN \"OrganizationId\" IS NOT NULL THEN 'organization:' || \"OrganizationId\"::text ELSE 'account:' || \"MailboxAccountId\" END\nAND \"Origin\" = CASE WHEN \"MailboxAccountId\" IS NULL THEN 'Asserted' ELSE 'Collected' END");

            migrationBuilder.AddForeignKey(
                name: "FK_contacts_organizations_OrganizationId",
                table: "contacts",
                column: "OrganizationId",
                principalTable: "organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_contacts_organizations_OrganizationId",
                table: "contacts");

            migrationBuilder.DropIndex(
                name: "IX_contacts_OrganizationId",
                table: "contacts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_contacts_book_holder",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "contacts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_contacts_book_holder",
                table: "contacts",
                sql: "((\"UserId\" IS NOT NULL)::int + (\"MailboxAccountId\" IS NOT NULL)::int) = 1\nAND \"BookHolderId\" = CASE WHEN \"UserId\" IS NULL THEN 'account:' || \"MailboxAccountId\" ELSE 'user:' || \"UserId\"::text END\nAND \"Origin\" = CASE WHEN \"UserId\" IS NULL THEN 'Collected' ELSE 'Asserted' END");
        }
    }
}
