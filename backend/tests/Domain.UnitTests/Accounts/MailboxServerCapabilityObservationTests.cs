// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;
using Xunit;

namespace MailFathom.Domain.UnitTests.Accounts;

public sealed class MailboxServerCapabilityObservationTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A custodian deciding for an account no session has reached must carry the act server-first.</summary>
    [Fact]
    public void NeverObserved_AnAccountNoSessionReached_AdvertisesNothingAndNamesNoPlacement()
    {
        // Act
        var observation = MailboxServerCapabilityObservation.NeverObserved;

        // Assert
        Assert.Equal(MailboxServerCapabilities.None, observation.Advertised);
        Assert.Null(observation.ObservedAt);
        Assert.False(observation.NamesWhereMessagesLand);
    }

    /// <summary>RFC 9051 folds <c>UIDPLUS</c> into <c>IMAP4rev2</c>, so either one is a server that names where a message landed.</summary>
    [Theory]
    [InlineData(MailboxServerCapabilities.UidPlus, true)]
    [InlineData(MailboxServerCapabilities.Imap4Rev2, true)]
    [InlineData(MailboxServerCapabilities.UidPlus | MailboxServerCapabilities.Imap4Rev2, true)]
    [InlineData(MailboxServerCapabilities.None, false)]
    public void NamesWhereMessagesLand_AnObservedServer_FollowsUidPlusOrImap4Rev2(
        MailboxServerCapabilities advertised,
        bool namesWhereMessagesLand)
    {
        // Act
        var observation = MailboxServerCapabilityObservation.Observed(advertised, ObservedAt);

        // Assert
        Assert.Equal(namesWhereMessagesLand, observation.NamesWhereMessagesLand);
        Assert.Equal(ObservedAt, observation.ObservedAt);
    }
}
