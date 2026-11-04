// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Delivery.Outbox;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Outbox;

public sealed class MailOutboxSettingsTests
{
    /// <summary>The bounds a deployment states are kept as they were stated.</summary>
    [Fact]
    public void Create_ValidBounds_KeepsThem()
    {
        // Act
        var settings = MailOutboxSettings.Create(
            maxDeliveriesPerPass: 10,
            leaseDuration: TimeSpan.FromMinutes(10),
            attemptTimeout: TimeSpan.FromMinutes(7),
            maxAttempts: 5,
            retryBaseDelay: TimeSpan.FromMinutes(1),
            retryMaxDelay: TimeSpan.FromHours(1),
            allowedLateness: TimeSpan.FromHours(8));

        // Assert
        Assert.Equal(10, settings.MaxDeliveriesPerPass);
        Assert.Equal(TimeSpan.FromMinutes(10), settings.LeaseDuration);
        Assert.Equal(TimeSpan.FromMinutes(7), settings.AttemptTimeout);
        Assert.Equal(5, settings.MaxAttempts);
        Assert.Equal(TimeSpan.FromMinutes(1), settings.RetryBaseDelay);
        Assert.Equal(TimeSpan.FromHours(1), settings.RetryMaxDelay);
        Assert.Equal(TimeSpan.FromHours(8), settings.AllowedLateness);
    }

    /// <summary>
    /// An attempt that may still be transmitting when its lease runs out is a second attempt taking a message the
    /// first may already have sent, so the ordering is refused rather than warned about.
    /// </summary>
    [Theory]
    [InlineData(10, 10)]
    [InlineData(10, 11)]
    public void Create_AttemptTimeoutReachesTheLeaseDuration_IsRefused(int leaseMinutes, int timeoutMinutes)
    {
        // Act
        var thrown = Assert.Throws<ArgumentOutOfRangeException>(() => MailOutboxSettings.Create(
            maxDeliveriesPerPass: 10,
            TimeSpan.FromMinutes(leaseMinutes),
            TimeSpan.FromMinutes(timeoutMinutes),
            maxAttempts: 5,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(8)));

        // Assert
        Assert.Equal("attemptTimeout", thrown.ParamName);
    }

    /// <summary>A ceiling below the delay it caps would shorten every retry instead of bounding the growth.</summary>
    [Fact]
    public void Create_RetryCeilingBelowItsBaseDelay_IsRefused()
    {
        // Act
        var thrown = Assert.Throws<ArgumentOutOfRangeException>(() => MailOutboxSettings.Create(
            maxDeliveriesPerPass: 10,
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(7),
            maxAttempts: 5,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(1),
            TimeSpan.FromHours(8)));

        // Assert
        Assert.Equal("retryMaxDelay", thrown.ParamName);
    }

    /// <summary>Every count and duration is a bound, and a bound of nothing is stated wrongly rather than meaning unlimited.</summary>
    [Theory]
    [InlineData(0, 5, "maxDeliveriesPerPass")]
    [InlineData(10, 0, "maxAttempts")]
    public void Create_CountIsNotPositive_IsRefused(int maxDeliveriesPerPass, int maxAttempts, string expectedParameter)
    {
        // Act
        var thrown = Assert.Throws<ArgumentOutOfRangeException>(() => MailOutboxSettings.Create(
            maxDeliveriesPerPass,
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(7),
            maxAttempts,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(8)));

        // Assert
        Assert.Equal(expectedParameter, thrown.ParamName);
    }

    /// <summary>A lateness bound of nothing would refuse every held send at the instant it became due, so it is stated wrongly.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_AllowedLatenessIsNotPositive_IsRefused(int minutes)
    {
        // Act
        var thrown = Assert.Throws<ArgumentOutOfRangeException>(() => MailOutboxSettings.Create(
            maxDeliveriesPerPass: 10,
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(7),
            maxAttempts: 5,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromHours(1),
            TimeSpan.FromMinutes(minutes)));

        // Assert
        Assert.Equal("allowedLateness", thrown.ParamName);
    }

    /// <summary>Bounds that name no retry window carry the fifteen minutes the product promises a person.</summary>
    [Fact]
    public void RetryWindow_NoneStated_IsFifteenMinutes()
    {
        // Act
        var settings = Defaults();

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(15), settings.RetryWindow);
    }

    /// <summary>A window of nothing would refuse every send on its first failure, so it is stated wrongly.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RetryWindow_NotPositive_IsRefused(int minutes)
    {
        // Act
        var thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => Defaults() with { RetryWindow = TimeSpan.FromMinutes(minutes) });

        // Assert
        Assert.Equal("value", thrown.ParamName);
    }

    /// <summary>A retry is admitted exactly while its whole attempt still ends by the time the window closes.</summary>
    [Theory]
    [InlineData(8 * 60, true)]
    [InlineData((8 * 60) + 1, false)]
    public void AdmitsRetry_AttemptStartingAfterTheFirstFailure_AdmitsItOnlyWhileItsAttemptEndsInsideTheWindow(
        int secondsAfterFirstFailure,
        bool expected)
    {
        // Arrange
        var settings = Defaults();
        var firstFailedAt = new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

        // Act
        var admitted = settings.AdmitsRetry(firstFailedAt, firstFailedAt.AddSeconds(secondsAfterFirstFailure));

        // Assert
        Assert.Equal(expected, admitted);
    }

    private static MailOutboxSettings Defaults() => MailOutboxSettings.Create(
        maxDeliveriesPerPass: 10,
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(7),
        maxAttempts: 5,
        TimeSpan.FromMinutes(1),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(8));
}
