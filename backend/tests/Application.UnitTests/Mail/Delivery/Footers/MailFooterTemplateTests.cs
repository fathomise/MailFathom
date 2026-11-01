// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Delivery.Footers;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Footers;

public sealed class MailFooterTemplateTests
{
    [Theory]
    [InlineData("<p>{{displayName}}</p>")]
    [InlineData("<p>{{ jobTitle }} · {{phoneNumber}}</p>")]
    [InlineData("<p>No variables at all</p>")]
    public void FindUnknownVariable_ATemplateNamingOnlyKnownVariables_FindsNone(string html)
    {
        // Act
        var unknown = MailFooterTemplate.FindUnknownVariable(html);

        // Assert
        Assert.Null(unknown);
    }

    [Fact]
    public void FindUnknownVariable_AMisspeltVariable_NamesItAsWritten()
    {
        // Act
        var unknown = MailFooterTemplate.FindUnknownVariable("<p>{{displayName}}, {{ jobtitle }}</p>");

        // Assert
        Assert.Equal("jobtitle", unknown);
    }

    [Fact]
    public void Fill_AMembersValues_ReplacesEveryVariableAndEncodesEachValueForMarkup()
    {
        // Arrange
        var sender = new MailFooterSender("Anna <Kowalska>", "Head of R&D", "+48 600 000 000");

        // Act
        var filled = MailFooterTemplate.Fill("<p>{{displayName}} | {{ jobTitle }} | {{phoneNumber}}</p>", sender);

        // Assert
        Assert.Equal("<p>Anna &lt;Kowalska&gt; | Head of R&amp;D | +48 600 000 000</p>", filled);
    }

    [Fact]
    public void Fill_AMemberWithoutAJobTitleOrAPhone_LeavesThoseVariablesEmpty()
    {
        // Arrange
        var sender = new MailFooterSender("Anna Kowalska", JobTitle: null, PhoneNumber: null);

        // Act
        var filled = MailFooterTemplate.Fill("<p>{{displayName}}[{{jobTitle}}][{{phoneNumber}}]</p>", sender);

        // Assert
        Assert.Equal("<p>Anna Kowalska[][]</p>", filled);
    }
}
