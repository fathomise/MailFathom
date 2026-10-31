// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.ThreadStates;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.ThreadStates;

/// <summary>Covers what a title line may hold before it is drawn where a sender's words are not.</summary>
public sealed class ThreadTitleTests
{
    [Theory]
    [InlineData("Racking quotation for the Gdańsk warehouse")]
    [InlineData("Wycena regałów: termin 28.08 [redacted:card]")]
    public void IsPlainLine_OneLineOfPlainText_IsAccepted(string value)
    {
        // Act
        var accepted = ThreadTitle.IsPlainLine(value, ThreadTitle.MaximumTopicLength);

        // Assert
        Assert.True(accepted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" Racking quotation")]
    [InlineData("Racking\nquotation")]
    [InlineData("<b>Racking</b> quotation")]
    [InlineData("**Racking** quotation")]
    [InlineData("`Racking` quotation")]
    [InlineData("[Racking](quotation)")]
    [InlineData("Racking at https://example.test")]
    [InlineData("Racking at www.example.test")]
    [InlineData("Write to mailto:someone")]
    public void IsPlainLine_MarkupALinkOrASecondLine_IsRefused(string value)
    {
        // Act
        var accepted = ThreadTitle.IsPlainLine(value, ThreadTitle.MaximumTopicLength);

        // Assert
        Assert.False(accepted);
    }

    [Fact]
    public void IsPlainLine_ALinePastItsBound_IsRefused()
    {
        // Act
        var accepted = ThreadTitle.IsPlainLine(new string('a', ThreadTitle.MaximumTopicLength + 1), ThreadTitle.MaximumTopicLength);

        // Assert
        Assert.False(accepted);
    }

    [Fact]
    public void Create_AnEmptyStanding_IsATitleWithNothingLeftOpen()
    {
        // Act
        var title = ThreadTitle.Create("Racking quotation", string.Empty);

        // Assert
        Assert.Empty(title.Standing);
    }

    [Fact]
    public void Create_ATopicThatIsNotAPlainLine_Throws()
    {
        // Act
        var creating = () => ThreadTitle.Create("<i>Racking</i>", string.Empty);

        // Assert
        Assert.Throws<ArgumentException>(creating);
    }
}
