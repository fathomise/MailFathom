// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Emails;

/// <summary>
/// Covers what a read by identifier asks PostgreSQL for. It is the one read family that does not pass through the shared
/// selection predicate — a message, its body, its source, an attachment, a reply's source — so it states the same two
/// exclusions on its own.
/// </summary>
public sealed class StoredEmailSummaryReaderCommandTests
{
    /// <summary>A message still settling answers as absent, so naming its identifier reads nothing a listing has not offered.</summary>
    [Fact]
    public void Readable_AnyRow_AdmitsOnlyMailThatHasSettled()
    {
        // Act
        var command = CommandOfReadable();

        // Assert
        Assert.Contains($"\"{nameof(StoredEmailEntity.SettledAt)}\" IS NOT NULL", command, StringComparison.Ordinal);
    }

    [Fact]
    public void Readable_AnyRow_AdmitsNoTombstonedMail()
    {
        // Act
        var command = CommandOfReadable();

        // Assert
        Assert.Contains($"\"{nameof(StoredEmailEntity.RemoteExpungeObservedAt)}\" IS NULL", command, StringComparison.Ordinal);
    }

    private static string CommandOfReadable()
    {
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        return StoredEmailSummaryReader.Readable(context.StoredEmails.AsNoTracking()).ToQueryString();
    }
}
