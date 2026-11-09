// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Jobs.Scheduling;
using MailFathom.Application.Mail.Retention;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Retention;

/// <summary>Covers the one schedule a deployment declares for the nightly walk.</summary>
public sealed class MailRetentionScheduleSourceTests
{
    [Fact]
    public async Task ReadSchedulesAsync_Always_DeclaresOneDeploymentWalkAtTheConfiguredRecurrence()
    {
        // Arrange
        Assert.True(JobRecurrence.TryParse("Daily at 02:00", out var recurrence, out _));
        var source = new MailRetentionScheduleSource(recurrence!);

        // Act
        var schedules = await source.ReadSchedulesAsync(TestContext.Current.CancellationToken);

        // Assert
        var declared = Assert.Single(schedules);

        Assert.Equal(JobType.WalkMailRetention, declared.Payload.JobType);
        Assert.Same(recurrence, declared.Recurrence);
        Assert.Null(declared.Account);
    }

    /// <summary>Each proposal names a walk of its own, so the occasion's one enqueue decides which name the chain carries.</summary>
    [Fact]
    public async Task ReadSchedulesAsync_TwoReads_NameTwoWalksBeginningAtTheStart()
    {
        // Arrange
        Assert.True(JobRecurrence.TryParse("Daily at 02:00", out var recurrence, out _));
        var source = new MailRetentionScheduleSource(recurrence!);

        // Act
        var one = await source.ReadSchedulesAsync(TestContext.Current.CancellationToken);
        var other = await source.ReadSchedulesAsync(TestContext.Current.CancellationToken);

        // Assert
        var first = Assert.IsType<WalkMailRetentionJobPayload>(one[0].Payload);
        var second = Assert.IsType<WalkMailRetentionJobPayload>(other[0].Payload);

        Assert.NotEqual(first.WalkId, second.WalkId);
        Assert.Null(first.ResumeAfter);
        Assert.Equal(one[0].Id, other[0].Id);
    }
}
