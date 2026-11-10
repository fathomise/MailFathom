// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Contacts;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Contacts;

/// <summary>Covers the SQL both halves of the tally stop at the ceiling with.</summary>
/// <remarks>
/// The count comes out the same whichever rows the limit keeps, so what these tests hold is that the limit names its
/// rows: EF Core logs a limit nothing orders on every deployment that compiles it, and the context these tests translate
/// with refuses one instead. What the counts are against real mail is <c>OrchestratedContactCollectionTests</c>'s.
/// </remarks>
public sealed class AuthoredMailTallyTests
{
    private const int Ceiling = 3;

    private static readonly MailAccountId Account = MailAccountId.Create("work");

    /// <summary>The ordering follows the distinct identifiers, since one placed before <c>Distinct</c> is erased.</summary>
    [Fact]
    public void IdentifiedUpTo_ACeiling_OrdersTheDistinctIdentifiersBeforeTheLimit()
    {
        // Arrange
        using var context = DesignTimeQueryContext.Create();
        var authored = AuthoredMailTally.Authored(context.StoredEmails.AsNoTracking(), Account, Author());

        // Act
        var sql = AuthoredMailTally.IdentifiedUpTo(authored, Ceiling).ToQueryString();

        // Assert
        Assert.Matches(@"(?s)DISTINCT.*ORDER BY \w+\.""InternetMessageId""\s+LIMIT", sql);
    }

    /// <summary>A copy carrying no identifier is counted per stored row, so the row identity is the order.</summary>
    [Fact]
    public void UnidentifiedUpTo_ACeiling_OrdersByTheStoredRowBeforeTheLimit()
    {
        // Arrange
        using var context = DesignTimeQueryContext.Create();
        var authored = AuthoredMailTally.Authored(context.StoredEmails.AsNoTracking(), Account, Author());

        // Act
        var sql = AuthoredMailTally.UnidentifiedUpTo(authored, Ceiling).ToQueryString();

        // Assert
        Assert.Matches(@"ORDER BY \w+\.""Id""\s+LIMIT", sql);
    }

    private static EmailAddress Author()
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, "ada@example.test", out var author));

        return author;
    }
}
