// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordWhatEachAccountsServerLastAdvertised : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ServerCapabilities",
                table: "mailbox_accounts",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValueSql: "'None'");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ServerCapabilitiesObservedAt",
                table: "mailbox_accounts",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ServerCapabilities",
                table: "mailbox_accounts");

            migrationBuilder.DropColumn(
                name: "ServerCapabilitiesObservedAt",
                table: "mailbox_accounts");
        }
    }
}
