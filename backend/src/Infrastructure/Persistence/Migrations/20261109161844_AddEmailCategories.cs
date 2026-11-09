// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "email_categories",
                columns: table => new
                {
                    StoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Probability = table.Column<double>(type: "double precision", nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Origin = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    QuestionRevision = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_categories", x => new { x.StoredEmailId, x.Kind, x.Category });
                    table.CheckConstraint("ck_email_categories_probability", "\"Probability\" >= 0 AND \"Probability\" <= 1");
                    table.ForeignKey(
                        name: "FK_email_categories_stored_emails_StoredEmailId",
                        column: x => x.StoredEmailId,
                        principalTable: "stored_emails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_email_categories_one_default",
                table: "email_categories",
                column: "StoredEmailId",
                unique: true,
                filter: "\"Kind\" = 'Default'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_categories");
        }
    }
}
