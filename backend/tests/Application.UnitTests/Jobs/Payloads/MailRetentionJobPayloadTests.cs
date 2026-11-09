// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Domain.Accounts;
using Xunit;

namespace MailFathom.Application.UnitTests.Jobs.Payloads;

/// <summary>Covers the keys that keep one night's walk and each account's run one execution apiece.</summary>
public sealed class MailRetentionJobPayloadTests
{
    private const string Walk = "walk-of-the-occasion";

    private static readonly MailAccountId Account = MailAccountId.Create("0199a0c0-0000-7000-8000-000000000001");

    /// <summary>A repeated attempt composes the key the first one did, so the hand-on is answered with the segment waiting.</summary>
    [Fact]
    public void ToIdempotencyKey_TheSameSegmentTwice_ComposesOneKey()
    {
        // Arrange
        var after = Guid.Parse("0199a0c0-0000-7000-8000-000000000010");
        var first = WalkMailRetentionJobPayload.FromTheStart(Walk);

        // Act
        var one = first.ContinuingAfter(after).ToIdempotencyKey();
        var other = first.ContinuingAfter(after).ToIdempotencyKey();

        // Assert
        Assert.Equal(one, other);
    }

    /// <summary>Two segments and two walks never share a key, because a key is unique for the life of the table.</summary>
    [Fact]
    public void ToIdempotencyKey_ADifferentSegmentOrWalk_ComposesAnotherKey()
    {
        // Arrange
        var first = WalkMailRetentionJobPayload.FromTheStart(Walk);
        var second = first.ContinuingAfter(Guid.Parse("0199a0c0-0000-7000-8000-000000000010"));
        var ofAnotherNight = WalkMailRetentionJobPayload.FromTheStart("walk-of-the-next-occasion");

        // Act
        JobIdempotencyKey[] keys = [first.ToIdempotencyKey(), second.ToIdempotencyKey(), ofAnotherNight.ToIdempotencyKey()];

        // Assert
        Assert.Equal(3, keys.Distinct().Count());
        Assert.Equal(1, second.Segment);
    }

    [Fact]
    public void FromTheStart_ABlankWalk_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => WalkMailRetentionJobPayload.FromTheStart("  "));
    }

    /// <summary>An account is run once per night however often the segment naming it is attempted.</summary>
    [Fact]
    public void For_OneAccountOfOneWalk_ComposesOneKeyAndKeepsTheAccount()
    {
        // Arrange
        var run = RetainMailAccountJobPayload.For(Walk, Account);

        // Act
        var key = RetainMailAccountJobPayload.For(Walk, Account).ToIdempotencyKey();

        // Assert
        Assert.Equal(run.ToIdempotencyKey(), key);
        Assert.Equal(Account, run.Account);
        Assert.Equal(JobType.RetainMailAccount, run.JobType);
    }
}
