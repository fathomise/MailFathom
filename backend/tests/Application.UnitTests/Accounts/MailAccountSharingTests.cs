// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Accounts;

public sealed class MailAccountSharingTests
{
    private static readonly MailAccountId Board = MailAccountId.Create("board");

    private static readonly MailAccountId Personal = MailAccountId.Create("personal");

    /// <summary>An account assigned to several users is shared, and one assigned to its reader alone is not.</summary>
    [Fact]
    public async Task ReadSharedAmongAsync_AnAccountSeveralUsersAreAssigned_IsSharedAndOneUsersAccountIsNot()
    {
        // Arrange
        var assignments = AssignmentsWhere(
            (Board, [SyntheticUser.Deployment, SyntheticUser.Another]),
            (Personal, [SyntheticUser.Deployment]));

        // Act
        var shared = await assignments.ReadSharedAmongAsync([Board, Personal], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Board, Assert.Single(shared));
    }

    /// <summary>An account assigned to nobody is not shared, which is what a user withheld from every assignment leaves behind.</summary>
    [Fact]
    public async Task ReadSharedAmongAsync_AnAccountAssignedToNobody_IsNotShared()
    {
        // Arrange
        var assignments = AssignmentsWhere((Board, []));

        // Act
        var shared = await assignments.ReadSharedAmongAsync([Board], TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(shared);
    }

    private static IMailAccountAssignments AssignmentsWhere(params (MailAccountId Account, UserId[] Users)[] relation)
    {
        var assignments = Substitute.For<IMailAccountAssignments>();
        foreach (var (account, users) in relation)
        {
            assignments.ReadUsersAssignedToAsync(account, Arg.Any<CancellationToken>()).Returns(users);
        }

        return assignments;
    }
}
