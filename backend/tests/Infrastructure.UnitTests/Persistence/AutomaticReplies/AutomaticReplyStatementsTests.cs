// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.AutomaticReplies;
using MailFathom.Infrastructure.Persistence.AutomaticReplies;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.AutomaticReplies;

/// <summary>
/// What the save and the claim promise across replicas is in their text: that a save starts the once-per-sender rule
/// over in the same statement, and that a claim under a revision a save replaced, or one a competing replica already
/// took, inserts nothing rather than failing. All of it is decidable without a server, so it is established here.
/// </summary>
public sealed class AutomaticReplyStatementsTests
{
    private static readonly Guid User = new("11111111-1111-1111-1111-111111111111");

    private static readonly Guid Revision = new("0199a0c0-0000-7000-8000-000000000002");

    private static readonly DateTimeOffset Now = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Two devices saving at once replace each other rather than colliding on the key, and a user this deployment no longer holds affects no row.</summary>
    [Fact]
    public void ComposeSave_TheWrite_UpsertsFromTheUserRow()
    {
        // Act
        var statement = ComposeSave().Format;

        // Assert
        Assert.Contains("FROM settings_accounts", statement, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (\"UserId\") DO UPDATE SET", statement, StringComparison.Ordinal);
        Assert.Contains("\"Revision\" = EXCLUDED.\"Revision\"", statement, StringComparison.Ordinal);
    }

    /// <summary>The answers an older revision recorded go in the same statement as the save, so no instant holds the new reply beside them.</summary>
    [Fact]
    public void ComposeSave_TheWrite_ForgetsTheSendersEveryOtherRevisionAnswered()
    {
        // Act
        var statement = ComposeSave().Format;

        // Assert
        Assert.Contains("DELETE FROM automatic_reply_answers", statement, StringComparison.Ordinal);
        Assert.Contains("AND \"Revision\" <> {", statement, StringComparison.Ordinal);
    }

    /// <summary>The person's words travel as parameters, never as statement text.</summary>
    [Fact]
    public void ComposeSave_TheTexts_AreParametersRatherThanStatementText()
    {
        // Act
        var statement = ComposeSave();

        // Assert
        Assert.DoesNotContain("I am away", statement.Format, StringComparison.Ordinal);
        Assert.Contains("I am away until Monday.", statement.GetArguments());
    }

    /// <summary>A claim reads the revision from the reply's own row and yields to one already taken, so the replica that lost inserts nothing.</summary>
    [Fact]
    public void ComposeClaim_TheInsert_IsTakenAgainstTheCurrentRevisionAndYieldsToAnEarlierClaim()
    {
        // Act
        var statement = AutomaticReplyStatements.ComposeClaim(User, Revision, "anna@example.test", Now).Format;

        // Assert
        Assert.Contains("FROM automatic_replies", statement, StringComparison.Ordinal);
        Assert.Contains("AND \"Revision\" = {", statement, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT DO NOTHING", statement, StringComparison.Ordinal);
    }

    private static FormattableString ComposeSave()
    {
        Assert.True(AutomaticReply.TryCreate(
            insideText: null,
            "I am away until Monday.",
            [MailAccountId.Create("primary")],
            Now,
            Now.AddDays(7),
            isOn: true,
            out var reply,
            out _));

        return AutomaticReplyStatements.ComposeSave(User, Revision, reply!, Now);
    }
}
