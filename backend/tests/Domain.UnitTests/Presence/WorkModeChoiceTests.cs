// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using MailFathom.Domain.Access;
using MailFathom.Domain.Presence;
using Xunit;

namespace MailFathom.Domain.UnitTests.Presence;

/// <summary>Covers where a member says they work from, for today alone or until they change it.</summary>
public sealed class WorkModeChoiceTests
{
    private static readonly UserTimeZone Warsaw =
        UserTimeZone.TryRead("Europe/Warsaw", out var zone) ? zone : throw new InvalidOperationException("The test host does not know Europe/Warsaw.");

    [Fact]
    public void Made_TodayOnly_EndsAtTheMembersOwnNextMidnight()
    {
        // Act
        var choice = WorkModeChoice.Made(WorkMode.Remote, todayOnly: true, Instant("2026-10-31T23:30:00Z"), Warsaw);

        // Assert
        Assert.Equal(new WorkModeChoice(WorkMode.Remote, Instant("2026-11-01T23:00:00Z")), choice);
    }

    [Fact]
    public void Made_UntilChanged_HasNoEnd()
    {
        // Act
        var choice = WorkModeChoice.Made(WorkMode.Office, todayOnly: false, Instant("2026-10-31T09:00:00Z"), Warsaw);

        // Assert
        Assert.Equal(new WorkModeChoice(WorkMode.Office, EndsAt: null), choice);
    }

    [Fact]
    public void IsInEffectAt_TodayOnlyOnceTheDayEnded_HoldsNoLonger()
    {
        // Arrange
        var choice = WorkModeChoice.Made(WorkMode.Office, todayOnly: true, Instant("2026-10-31T09:00:00Z"), Warsaw);

        // Act, Assert
        Assert.True(choice.IsInEffectAt(Instant("2026-10-31T22:59:59Z")));
        Assert.False(choice.IsInEffectAt(Instant("2026-10-31T23:00:00Z")));
    }

    private static DateTimeOffset Instant(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
