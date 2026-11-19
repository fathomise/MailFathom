// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Tasks;
using Xunit;

namespace MailFathom.Domain.UnitTests.Tasks;

/// <summary>Covers the block of time a task may set aside.</summary>
public sealed class TaskPlanTests
{
    private static readonly DateTimeOffset StartAt = new(2026, 11, 20, 9, 0, 0, TimeSpan.FromHours(1));

    /// <summary>A block shorter than the floor, or longer than half a day, names no plan.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(721)]
    public void Create_APlanOutsideFiveMinutesToTwelveHours_IsRefused(int minutes)
    {
        // Act
        var creating = () => TaskPlan.Create(StartAt, minutes);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(creating);
    }

    /// <summary>Both ends of the range are plans.</summary>
    [Theory]
    [InlineData(TaskPlan.ShortestMinutes)]
    [InlineData(TaskPlan.LongestMinutes)]
    public void Create_APlanAtEitherEndOfTheRange_KeepsItsStartAndLength(int minutes)
    {
        // Act
        var plan = TaskPlan.Create(StartAt, minutes);

        // Assert
        Assert.Equal(StartAt, plan.StartAt);
        Assert.Equal(minutes, plan.Minutes);
    }
}
