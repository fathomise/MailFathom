// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Tasks;
using Xunit;

namespace MailFathom.Domain.UnitTests.Tasks;

/// <summary>Covers the person a task is with: the address they are reached at and the name they are drawn with.</summary>
public sealed class TaskCounterpartTests
{
    /// <summary>A name that says nothing is no name, so the counterpart is drawn by its address instead.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_ABlankDisplayName_HoldsNoName(string? displayName)
    {
        // Act
        var counterpart = new TaskCounterpart(Address(), displayName);

        // Assert
        Assert.Null(counterpart.DisplayName);
    }

    /// <summary>The name is stored in a bounded column, so one past it is refused rather than cut.</summary>
    [Fact]
    public void Constructor_ADisplayNamePastTheStoredBound_IsRefused()
    {
        // Act
        var creating = () => new TaskCounterpart(Address(), new string('n', TaskCounterpart.MaximumDisplayNameLength + 1));

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(creating);
    }

    /// <summary>The address is stored in a bounded column too, so one past it is refused here rather than by the database.</summary>
    [Fact]
    public void Constructor_AnAddressPastTheStoredBound_IsRefused()
    {
        // Arrange
        var local = new string('a', TaskCounterpart.MaximumAddressLength - "@example.org".Length + 1);
        Assert.True(EmailAddress.TryCreate(displayName: null, local + "@example.org", out var address));

        // Act
        var creating = () => new TaskCounterpart(address, "Anna Example");

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(creating);
    }

    /// <summary>The struct default reaches nobody, so a counterpart is never held at it.</summary>
    [Fact]
    public void Constructor_TheDefaultAddress_IsRefused()
    {
        // Act
        var creating = () => new TaskCounterpart(default, "Anna Example");

        // Assert
        Assert.Throws<ArgumentException>(creating);
    }

    private static EmailAddress Address() =>
        EmailAddress.TryCreate(displayName: null, "anna@example.org", out var address)
            ? address
            : throw new InvalidOperationException("The fixture address is usable.");
}
