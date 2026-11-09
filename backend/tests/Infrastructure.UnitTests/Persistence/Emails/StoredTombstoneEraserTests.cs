// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Retention;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Emails;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Emails;

/// <summary>Holds the batch query to something the provider can translate, which would otherwise first fail in the night.</summary>
public sealed class StoredTombstoneEraserTests
{
    /// <summary>Which rows it selects is proved against PostgreSQL in the integration suite; this says it reaches PostgreSQL at all.</summary>
    [Fact]
    public void Aged_EveryClauseAtOnce_TranslatesToOneBoundedStatement()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
        var retention = new MailTombstoneRetention(
            IsEnabled: true,
            TimeSpan.FromDays(30),
            ["ARCHIVE", "0199c1a2-0000-7000-8000-0000000000aa"]);
        var moment = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        // Act
        var statement = StoredTombstoneEraser
            .Aged(context, MailAccountId.Create("work"), retention, moment, limit: 100)
            .ToQueryString();

        // Assert
        Assert.Contains("COALESCE", statement, StringComparison.Ordinal);
        Assert.Contains("\"AuthoredDeleteFlaggedAt\"", statement, StringComparison.Ordinal);
        Assert.Contains("\"IsRetainedAfterAuthoredDelete\"", statement, StringComparison.Ordinal);
        Assert.Contains("NOT EXISTS", statement, StringComparison.Ordinal);
        Assert.Contains("LIMIT", statement, StringComparison.Ordinal);
    }
}
