// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Spam.Gating;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.ThreadStates;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.ThreadStates;

/// <summary>
/// Proves the reader of conversations awaiting a state is a query PostgreSQL runs rather than one the provider refuses.
/// </summary>
/// <remarks>
/// <para>
/// The pass this reader opens is the whole of the derivation, and a clause the provider cannot express ends it before a
/// single conversation is read — which is what happened: the batch was ordered by a member of a constructed row, the
/// provider refused to reduce that back to the grouped column, and every run of every account ended on the same
/// exception with nothing but a log line to say so.
/// </para>
/// <para>
/// The shape is worth the assertion because it is not a filtered projection: a grouping over an account's mail, a left
/// join to the states already stored, a negated existence condition over a collection navigation for the moves that
/// have not settled, and a bound taken after an ordering. No database is needed to establish any of it — the statement
/// the provider would send is printable, and its absence is the failure.
/// </para>
/// </remarks>
public sealed class ThreadAwaitingStateQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Every clause reaches the server, including the ordering the bound is taken after.</summary>
    [Fact]
    public void Awaiting_TheBatchTheDerivationPassReads_TranslatesToOneBoundedStatement()
    {
        // Arrange
        var options = MailFathomDbContextDesignTimeFactory.BuildOptions(
            orchestratedConnectionString: null,
            designTimeConnectionString: null);
        using var context = new MailFathomDbContext(options, PostgresTextSearchConfiguration.Default);

        var counted = StoredThreadStateStore.Selecting(
            context.StoredEmails.AsNoTracking(),
            "work",
            [new MailFolderIdentity(MailAccountId.Create("work"), MailFolderAlias.Create("INBOX"))],
            new DerivedWorkAdmissionTerms([], [], [], Now),
            [Guid.CreateVersion7()]);

        // Act
        var sql = StoredThreadStateStore.Awaiting(counted, context.EmailThreadStates.AsNoTracking())
            .Take(8)
            .ToQueryString();

        // Assert
        Assert.Contains("GROUP BY", sql, StringComparison.Ordinal);
        Assert.Contains("NOT EXISTS (", sql, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN", sql, StringComparison.Ordinal);

        var ordering = sql.IndexOf("ORDER BY", StringComparison.Ordinal);
        var bound = sql.IndexOf("LIMIT", StringComparison.Ordinal);

        Assert.True(ordering >= 0, sql);
        Assert.True(bound > ordering, sql);

        // The topic the stored state kept travels with the batch, so the derivation can be asked to keep it.
        Assert.Contains("\"Topic\"", sql, StringComparison.Ordinal);

        // A message in Trash, or in a folder beneath it, counts towards nothing, so it neither shapes the state nor
        // names the conversation.
        Assert.Contains("trashedFolderIds", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// The read of one conversation's state asks the pass's own question of that conversation alone, so the grouping and
    /// the comparison reach the server narrowed to it rather than as a scan of the account.
    /// </summary>
    [Fact]
    public void OwedADerivation_TheQuestionOneReadAsksOfItsConversation_TranslatesToTheSelectionNarrowedToIt()
    {
        // Arrange
        var options = MailFathomDbContextDesignTimeFactory.BuildOptions(
            orchestratedConnectionString: null,
            designTimeConnectionString: null);
        using var context = new MailFathomDbContext(options, PostgresTextSearchConfiguration.Default);

        var conversation = Guid.CreateVersion7();
        var counted = StoredThreadStateReader.Counted(
            context.StoredEmails.AsNoTracking(),
            conversation,
            "work",
            [new MailFolderIdentity(MailAccountId.Create("work"), MailFolderAlias.Create("INBOX"))],
            new DerivedWorkAdmissionTerms([], [], [], Now),
            []);

        // Act
        var sql = StoredThreadStateReader.OwedADerivation(counted, context.EmailThreadStates.AsNoTracking(), conversation)
            .ToQueryString();
        var leftTheSelection = counted.Select(static email => email.Id).Take(1).ToQueryString();

        // Assert
        Assert.Contains("GROUP BY", sql, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN", sql, StringComparison.Ordinal);
        Assert.Contains("\"DerivedFromMessageCount\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"DerivedFromLatestArrival\"", sql, StringComparison.Ordinal);

        // Whether the conversation still has a message the pass counts is asked of the same narrowing on its own.
        Assert.Contains("LIMIT", leftTheSelection, StringComparison.Ordinal);
    }
}
