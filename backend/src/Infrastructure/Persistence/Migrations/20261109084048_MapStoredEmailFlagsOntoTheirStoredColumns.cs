// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Records the stored email flags under the names <c>IsMarkedSeen</c>, <c>IsFlagged</c>, <c>IsDraft</c>,
    /// <c>Keywords</c>, and <c>FlagsObservedAt</c> while their columns keep the names they were created with, so the
    /// model snapshot moves and PostgreSQL is asked for nothing.
    /// </summary>
    /// <remarks>
    /// Renaming a column would break the previous build, which a rolling upgrade runs against the same table.
    /// </remarks>
    public partial class MapStoredEmailFlagsOntoTheirStoredColumns : Migration
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
