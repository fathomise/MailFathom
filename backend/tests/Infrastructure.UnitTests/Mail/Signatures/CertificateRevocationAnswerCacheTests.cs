// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails.Signatures;
using MailFathom.Infrastructure.Mail.Signatures;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Signatures;

/// <summary>
/// An answer held past the moment it stops being true keeps a revoked certificate verified, and an unavailable answer
/// held for good keeps a recovered responder unasked, so both lifetimes are bounded and each bound is shown here.
/// </summary>
public sealed class CertificateRevocationAnswerCacheTests
{
    private const string CertificateKey = "issuer-key:1001";

    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly CertificateRevocationAnswer Good =
        new(CertificateRevocationStatus.Good, RevocationCheckMethod.Ocsp, Now, RevokedAt: null);

    [Fact]
    public void TryRead_GoodAnswerValidForTwoDays_IsHeldForOneDayAndNoLonger()
    {
        // Arrange
        var clock = new FakeTimeProvider(Now);
        var cache = new CertificateRevocationAnswerCache(clock);
        cache.Store(CertificateKey, Good, Now.AddDays(2));

        // Act
        clock.Advance(TimeSpan.FromDays(1) - TimeSpan.FromSeconds(1));
        var heldJustBeforeADay = cache.TryRead(CertificateKey, out _);
        clock.Advance(TimeSpan.FromSeconds(1));
        var heldAtADay = cache.TryRead(CertificateKey, out _);

        // Assert
        Assert.True(heldJustBeforeADay);
        Assert.False(heldAtADay);
    }

    [Fact]
    public void TryRead_GoodAnswerValidForThirtySeconds_IsHeldForAMinute()
    {
        // Arrange
        var clock = new FakeTimeProvider(Now);
        var cache = new CertificateRevocationAnswerCache(clock);
        cache.Store(CertificateKey, Good, Now.AddSeconds(30));

        // Act
        clock.Advance(TimeSpan.FromSeconds(59));
        var heldAtFiftyNineSeconds = cache.TryRead(CertificateKey, out var held);
        clock.Advance(TimeSpan.FromSeconds(1));
        var heldAtAMinute = cache.TryRead(CertificateKey, out _);

        // Assert
        Assert.True(heldAtFiftyNineSeconds);
        Assert.Equal(Good, held);
        Assert.False(heldAtAMinute);
    }

    [Fact]
    public void TryRead_UnavailableAnswer_IsRememberedForAMinuteAndNoLonger()
    {
        // Arrange
        var clock = new FakeTimeProvider(Now);
        var cache = new CertificateRevocationAnswerCache(clock);
        cache.Store(CertificateKey, CertificateRevocationAnswer.Unavailable, validUntil: null);

        // Act
        clock.Advance(TimeSpan.FromSeconds(59));
        var heldAtFiftyNineSeconds = cache.TryRead(CertificateKey, out _);
        clock.Advance(TimeSpan.FromSeconds(1));
        var heldAtAMinute = cache.TryRead(CertificateKey, out _);

        // Assert
        Assert.True(heldAtFiftyNineSeconds);
        Assert.False(heldAtAMinute);
    }
}
