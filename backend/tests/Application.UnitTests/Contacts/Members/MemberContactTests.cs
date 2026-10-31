// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Contacts.Members;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Application.UnitTests.Contacts.Members;

/// <summary>Covers how a member's preferred address is resolved when the directory is read, from the addresses that are theirs at that moment.</summary>
public sealed class MemberContactTests
{
    /// <summary>A stated address that is one of the member's is the one their colleagues write to, in the spelling the account carries.</summary>
    [Fact]
    public void PreferredAddressAmong_AnAddressTheMemberHolds_IsTheStatedOne()
    {
        // Arrange
        EmailAddress[] addresses = [Address("anna@example.test"), Address("Anna.Kowalska@example.test")];

        // Act
        var preferred = MemberContact.PreferredAddressAmong(addresses, "anna.kowalska@EXAMPLE.test");

        // Assert
        Assert.Equal("Anna.Kowalska@example.test", preferred?.Address);
    }

    /// <summary>
    /// An account taken away from the member, or shared with somebody else since, is no longer theirs, so the stated
    /// address falls back to the first one that still is rather than naming a mailbox they no longer read alone.
    /// </summary>
    [Fact]
    public void PreferredAddressAmong_AnAddressNoLongerTheMembers_IsTheFirstThatIs()
    {
        // Arrange
        EmailAddress[] addresses = [Address("anna@example.test"), Address("kowalska@example.test")];

        // Act
        var preferred = MemberContact.PreferredAddressAmong(addresses, "office@example.test");

        // Assert
        Assert.Equal("anna@example.test", preferred?.Address);
    }

    /// <summary>Text that is no address at all is read as nothing stated, rather than as a refusal of the whole directory.</summary>
    [Fact]
    public void PreferredAddressAmong_TextThatIsNoAddress_IsTheFirstAddress()
    {
        // Arrange
        EmailAddress[] addresses = [Address("anna@example.test")];

        // Act
        var preferred = MemberContact.PreferredAddressAmong(addresses, "not an address");

        // Assert
        Assert.Equal("anna@example.test", preferred?.Address);
    }

    /// <summary>A member with no mailbox of their own has no address to prefer, whatever their record says.</summary>
    [Fact]
    public void PreferredAddressAmong_NoAddressAtAll_IsNothing()
    {
        // Act
        var preferred = MemberContact.PreferredAddressAmong([], "anna@example.test");

        // Assert
        Assert.Null(preferred);
    }

    private static EmailAddress Address(string address) =>
        EmailAddress.TryCreate(displayName: null, address, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"The test address '{address}' names no mailbox.");
}
