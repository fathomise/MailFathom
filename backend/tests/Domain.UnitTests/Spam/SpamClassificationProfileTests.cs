// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Spam;
using Xunit;

namespace MailFathom.Domain.UnitTests.Spam;

/// <summary>Covers what the identity is derived from, and what it deliberately leaves out.</summary>
public sealed class SpamClassificationProfileTests
{
    [Fact]
    public void Create_DerivedTwice_IsTheSameIdentity()
    {
        // Arrange, Act
        var first = SpamClassificationProfile.Create();
        var second = SpamClassificationProfile.Create();

        // Assert
        Assert.Equal(first, second);
        Assert.Equal(SpamClassificationProfile.LengthInCharacters, first.Value.Length);
    }

    /// <summary>The identity is pinned, because a record compares against it and a silent change rescores every mailbox.</summary>
    [Fact]
    public void Create_TheDeterministicTerms_DeriveTheirPinnedIdentity()
    {
        // Act
        var profile = SpamClassificationProfile.Create();

        // Assert
        Assert.Equal("0badac3c6df4", profile.Value);
    }

    [Fact]
    public void Restore_AnIdentityThisTypeDerived_ComparesEqualToIt()
    {
        // Arrange
        var derived = SpamClassificationProfile.Create();

        // Act
        var restored = SpamClassificationProfile.Restore(derived.Value);

        // Assert
        Assert.Equal(derived, restored);
        Assert.True(restored.IsSpecified);
    }

    [Theory]
    [InlineData("ABCDEF012345")]
    [InlineData("abcdef01234")]
    [InlineData("abcdef0123456")]
    [InlineData("abcdefg12345")]
    public void Restore_AValueThisTypeCouldNotHaveProduced_IsRefused(string value)
    {
        // Arrange, Act, Assert
        var failure = Assert.Throws<ArgumentException>(() => SpamClassificationProfile.Restore(value));

        Assert.Equal("value", failure.ParamName);
    }

    /// <summary>The struct default names no terms, which is what a record written before the profile existed reads as.</summary>
    [Fact]
    public void IsSpecified_TheStructDefault_NamesNoTerms()
    {
        // Arrange
        var unspecified = default(SpamClassificationProfile);

        // Act, Assert
        Assert.False(unspecified.IsSpecified);
        Assert.Throws<InvalidOperationException>(() => unspecified.Value);
        Assert.Equal("(unspecified)", unspecified.ToString());
    }
}
