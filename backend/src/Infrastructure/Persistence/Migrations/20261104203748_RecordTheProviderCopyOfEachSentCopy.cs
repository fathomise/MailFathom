// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordTheProviderCopyOfEachSentCopy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outgoing_email_filings_recent_by_filing",
                table: "outgoing_email_filings");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProviderCopySettledAt",
                table: "outgoing_email_filings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ProviderCopyUid",
                table: "outgoing_email_filings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ProviderCopyUidValidity",
                table: "outgoing_email_filings",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_email_filings_provider_copy",
                table: "outgoing_email_filings",
                columns: new[] { "MailboxAccountId", "AppendedAt", "OutgoingEmailId" },
                filter: "\"ProviderCopyUid\" IS NOT NULL AND \"ProviderCopySettledAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outgoing_email_filings_provider_copy",
                table: "outgoing_email_filings");

            migrationBuilder.DropColumn(
                name: "ProviderCopySettledAt",
                table: "outgoing_email_filings");

            migrationBuilder.DropColumn(
                name: "ProviderCopyUid",
                table: "outgoing_email_filings");

            migrationBuilder.DropColumn(
                name: "ProviderCopyUidValidity",
                table: "outgoing_email_filings");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_email_filings_recent_by_filing",
                table: "outgoing_email_filings",
                columns: new[] { "MailboxAccountId", "Filing", "AppendedAt", "OutgoingEmailId" },
                filter: "\"Stage\" = 'Confirmed'");
        }
    }
}
