// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;
using Xunit;

namespace MailFathom.Domain.UnitTests.Access;

public sealed class WorkingHoursTests
{
    /// <summary>A day named twice is one day, and the days read Monday first whatever order they were stated in.</summary>
    [Fact]
    public void Create_DaysInAnyOrderWithARepeat_HoldsEachOnceMondayFirst()
    {
        // Act
        var hours = WorkingHours.Create(
            [DayOfWeek.Sunday, DayOfWeek.Wednesday, DayOfWeek.Monday, DayOfWeek.Wednesday],
            new TimeOnly(8, 0),
            new TimeOnly(17, 0));

        // Assert
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Sunday], hours.Days);
        Assert.Equal(new TimeOnly(8, 0), hours.Start);
        Assert.Equal(new TimeOnly(17, 0), hours.End);
    }

    [Fact]
    public void Create_NoDay_IsRefusedNamingTheRule()
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(() => WorkingHours.Create([], new TimeOnly(8, 0), new TimeOnly(17, 0)));

        // Assert
        Assert.StartsWith("Working hours name at least one day.", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_AValueNamingNoWeekday_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(
            () => WorkingHours.Create([(DayOfWeek)7], new TimeOnly(8, 0), new TimeOnly(17, 0)));

        // Assert
        Assert.StartsWith("Every working day is a day of the week.", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>A range ending at or before its start would be one across midnight, which shifts would need and nothing here models.</summary>
    [Theory]
    [InlineData(17, 8)]
    [InlineData(9, 9)]
    [InlineData(22, 0)]
    public void Create_AnEndNotAfterTheStart_IsRefusedNamingTheRule(int startHour, int endHour)
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(
            () => WorkingHours.Create([DayOfWeek.Monday], new TimeOnly(startHour, 0), new TimeOnly(endHour, 0)));

        // Assert
        Assert.StartsWith("Working hours end after they start", refusal.Message, StringComparison.Ordinal);
    }
}
