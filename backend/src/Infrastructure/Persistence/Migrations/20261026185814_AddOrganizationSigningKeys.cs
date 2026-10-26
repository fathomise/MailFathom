// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationSigningKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "organization_signing_keys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicKey = table.Column<byte[]>(type: "bytea", nullable: false),
                    SealedPrivateKey = table.Column<byte[]>(type: "bytea", nullable: true),
                    DataEncryptionKeyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RetiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_signing_keys", x => x.Id);
                    table.CheckConstraint("ck_organization_signing_keys_sealed_pair", "(\"SealedPrivateKey\" IS NULL) = (\"DataEncryptionKeyId\" IS NULL)");
                    table.CheckConstraint("ck_organization_signing_keys_spent_holds_no_private_key", "(\"RetiredAt\" IS NULL AND \"RevokedAt\" IS NULL) OR \"SealedPrivateKey\" IS NULL");
                    table.ForeignKey(
                        name: "fk_organization_signing_keys_organization",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_organization_signing_keys_active",
                table: "organization_signing_keys",
                column: "OrganizationId",
                unique: true,
                filter: "\"RetiredAt\" IS NULL AND \"RevokedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_organization_signing_keys_organization",
                table: "organization_signing_keys",
                columns: new[] { "OrganizationId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "organization_signing_keys");
        }
    }
}
