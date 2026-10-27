// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Records the folder setting's member under the name <c>IsVisibleToAI</c> while its column keeps the name
    /// <c>IsVisibleToTools</c>, so the model snapshot moves and PostgreSQL is asked for nothing.
    /// </summary>
    /// <remarks>
    /// Renaming the column would break the previous build, which a rolling upgrade runs against the same table.
    /// </remarks>
    public partial class MapFolderVisibilityToAIOntoItsStoredColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
