// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKnownAuthenticationProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "known_authentication_provider_changes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ImapHost = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    AuthorityIdentifier = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    Act = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ChangedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_known_authentication_provider_changes", x => x.Id);
                    table.CheckConstraint("ck_known_authentication_provider_changes_act", "\"Act\" IN ('added', 'removed')");
                });

            migrationBuilder.CreateTable(
                name: "known_authentication_providers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ImapHost = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    AuthorityIdentifier = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    AddedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_known_authentication_providers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_known_authentication_providers_host",
                table: "known_authentication_providers",
                column: "ImapHost",
                unique: true);

            // The providers verified when this release was cut, under the test ADR 0043 states. Google strips an incoming
            // Authentication-Results header carrying mx.google.com and writes that identifier on every message delivered
            // to a mailbox read from imap.gmail.com, Gmail and Google Workspace alike. A provider joins this seed only
            // through a later migration after the same verification; the change record names the seed as its author.
            migrationBuilder.Sql(
                """
                INSERT INTO known_authentication_providers ("Id", "ImapHost", "AuthorityIdentifier", "AddedBy", "AddedAt")
                VALUES ('019a2731-0000-7000-8000-000000000001', 'imap.gmail.com', 'mx.google.com', 'migration AddKnownAuthenticationProviders', now());

                INSERT INTO known_authentication_provider_changes ("Id", "ImapHost", "AuthorityIdentifier", "Act", "ChangedBy", "ChangedAt")
                VALUES ('019a2731-0000-7000-8000-000000000002', 'imap.gmail.com', 'mx.google.com', 'added', 'migration AddKnownAuthenticationProviders', now());
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "known_authentication_provider_changes");

            migrationBuilder.DropTable(
                name: "known_authentication_providers");
        }
    }
}
