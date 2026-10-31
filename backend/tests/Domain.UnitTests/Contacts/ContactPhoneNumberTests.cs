// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Contacts;
using Xunit;

namespace MailFathom.Domain.UnitTests.Contacts;

public sealed class ContactPhoneNumberTests
{
    /// <summary>A number is kept in the form the user grouped it in, with only the surrounding whitespace gone.</summary>
    [Theory]
    [InlineData("  +1 202 555 0143 ", "+1 202 555 0143")]
    [InlineData("(22) 555-41.00", "(22) 555-41.00")]
    [InlineData("112", "112")]
    public void Create_ANumberAsTyped_KeepsItTrimmedAndOtherwiseUnchanged(string typed, string kept)
    {
        // Act
        var phoneNumber = ContactPhoneNumber.Create(typed, ContactPhoneLabel.Work);

        // Assert
        Assert.Equal(kept, phoneNumber.Number);
        Assert.Equal(ContactPhoneLabel.Work, phoneNumber.Label);
    }

    /// <summary>Thirty-two characters is the longest number admitted, and one more is refused.</summary>
    [Fact]
    public void Create_TheLongestNumberAndOneLonger_AdmitsTheFirstAndRefusesTheSecond()
    {
        // Arrange
        var longest = new string('1', ContactPhoneNumber.MaximumLength);

        // Act
        var admitted = ContactPhoneNumber.Create(longest, ContactPhoneLabel.Mobile);

        // Assert
        Assert.Equal(longest, admitted.Number);
        Assert.Throws<ArgumentException>(() => ContactPhoneNumber.Create(longest + "1", ContactPhoneLabel.Mobile));
    }

    /// <summary>Text with no digit, or with a character no number is grouped with, is not a number.</summary>
    [Theory]
    [InlineData("call me")]
    [InlineData("+ ( ) - .")]
    [InlineData("+1 202 555 0143 ext")]
    [InlineData("+1/202/555/0143")]
    [InlineData("+４８ ６０１")]
    public void Create_TextThatIsNotANumber_IsRefused(string typed)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => ContactPhoneNumber.Create(typed, ContactPhoneLabel.Home));
    }

    /// <summary>A label outside the closed set is refused rather than stored as a number nobody can name.</summary>
    [Fact]
    public void Create_ALabelNamingNoDeclaredValue_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => ContactPhoneNumber.Create("112", (ContactPhoneLabel)4));
    }
}
