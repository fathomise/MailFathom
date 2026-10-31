// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Access;

/// <summary>Covers the line of managers derived from the one manager each member names.</summary>
public sealed class ReportingLineTests
{
    private static readonly UserId Top = SyntheticUser.Deployment;

    private static readonly UserId Middle = SyntheticUser.Another;

    private static readonly UserId Bottom = SyntheticUser.Third;

    [Fact]
    public void ManagersAbove_AMemberAtTheBottomOfALine_ListsEveryManagerNearestFirst()
    {
        // Arrange
        var managers = new Dictionary<UserId, UserId> { [Bottom] = Middle, [Middle] = Top };

        // Act
        var above = ReportingLine.ManagersAbove(Bottom, managers);

        // Assert
        Assert.Equal([Middle, Top], above);
    }

    /// <summary>A loop the store never should hold still ends the walk rather than hanging the reader.</summary>
    [Fact]
    public void ManagersAbove_ALoopInTheNamedManagers_StopsBeforeRepeatingAnybody()
    {
        // Arrange
        var managers = new Dictionary<UserId, UserId> { [Bottom] = Middle, [Middle] = Top, [Top] = Bottom };

        // Act
        var above = ReportingLine.ManagersAbove(Bottom, managers);

        // Assert
        Assert.Equal([Middle, Top], above);
    }

    [Fact]
    public void WouldCycle_NamingTheMemberThemselves_IsACycle()
    {
        // Arrange & Act
        var cycle = ReportingLine.WouldCycle(Middle, Middle, new Dictionary<UserId, UserId>());

        // Assert
        Assert.True(cycle);
    }

    [Fact]
    public void WouldCycle_NamingSomebodyWhoReportsToTheMemberDirectlyOrThroughOthers_IsACycle()
    {
        // Arrange
        var managers = new Dictionary<UserId, UserId> { [Bottom] = Middle, [Middle] = Top };

        // Act
        var direct = ReportingLine.WouldCycle(Middle, Bottom, managers);
        var throughOthers = ReportingLine.WouldCycle(Top, Bottom, managers);

        // Assert
        Assert.True(direct);
        Assert.True(throughOthers);
    }

    [Fact]
    public void WouldCycle_NamingSomebodyAboveOrBesideTheMember_IsNoCycle()
    {
        // Arrange
        var managers = new Dictionary<UserId, UserId> { [Bottom] = Middle, [Middle] = Top };

        // Act
        var above = ReportingLine.WouldCycle(Bottom, Top, managers);
        var unrelated = ReportingLine.WouldCycle(Top, UserId.Create(new Guid("44444444-4444-4444-4444-444444444444")), managers);

        // Assert
        Assert.False(above);
        Assert.False(unrelated);
    }
}
