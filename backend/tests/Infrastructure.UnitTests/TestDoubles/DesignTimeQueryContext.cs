// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Connections;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MailFathom.Infrastructure.UnitTests.TestDoubles;

/// <summary>Builds a context that translates a query to SQL without a database, and refuses a row limit nothing orders.</summary>
/// <remarks>
/// EF Core only logs <see cref="CoreEventId.RowLimitingOperationWithoutOrderByWarning" />, once per query shape, so a
/// <c>Skip</c> or <c>Take</c> whose rows are up to the query plan reaches a deployment's log rather than a test. Thrown
/// here instead, it fails the translation test that composes the query.
/// </remarks>
internal static class DesignTimeQueryContext
{
    /// <summary>Creates a context over the design-time options, which never opens a connection.</summary>
    /// <returns>A context the caller disposes.</returns>
    public static MailFathomDbContext Create() => new(
        new DbContextOptionsBuilder<MailFathomDbContext>(
                MailFathomDbContextDesignTimeFactory.BuildOptions(
                    orchestratedConnectionString: null,
                    designTimeConnectionString: null))
            .ConfigureWarnings(warnings => warnings.Throw(CoreEventId.RowLimitingOperationWithoutOrderByWarning))
            .Options,
        PostgresTextSearchConfiguration.Default);
}
