// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentConversationReadThrough : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ReadThrough",
                table: "agent_conversations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Every conversation that existed before read state did was read as far as it had reached, so an upgrade
            // reports nothing as unread that nobody could have left unread.
            migrationBuilder.Sql("""UPDATE "agent_conversations" SET "ReadThrough" = "Sequence";""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReadThrough",
                table: "agent_conversations");
        }
    }
}
