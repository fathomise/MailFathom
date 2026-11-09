// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using MailFathom.Domain.Categories;
using Xunit;

namespace MailFathom.Domain.UnitTests.Categories;

/// <summary>Covers the published identities of the default categories.</summary>
public sealed class DefaultMailCategoryTests
{
    /// <summary>Written out rather than derived, because these are what stored records and account records already say: a rename of a member must leave them alone.</summary>
    [Fact]
    public void All_PublishesTheSevenIdentitiesStoredRecordsAndAccountRecordsName()
    {
        // Act
        var identities = DefaultMailCategory.All.Select(category => category.ToString());

        // Assert
        Assert.Equal(
            ["correspondence", "subscriptions", "finance", "shopping", "travel", "notifications", "other"],
            identities);
    }

    [Theory]
    [InlineData("finance", true)]
    [InlineData("Finance", false)]
    [InlineData("invoices", false)]
    [InlineData(null, false)]
    public void TryParse_AnIdentity_AcceptsOnlyADeclaredOneExactly(string? identity, bool declared)
    {
        // Act
        var parsed = DefaultMailCategory.TryParse(identity, out var category);

        // Assert
        Assert.Equal(declared, parsed);
        Assert.Equal(declared, category.IsSpecified);
    }

    [Fact]
    public void Default_ReportsItselfAsUnspecified()
    {
        // Arrange
        DefaultMailCategory unspecified = default;

        // Act
        var text = unspecified.ToString();

        // Assert
        Assert.False(unspecified.IsSpecified);
        Assert.Equal("(unspecified)", text);
    }

    [Fact]
    public void Json_AValue_RoundTripsAsItsIdentityAndAsAPropertyName()
    {
        // Arrange
        var categories = new Dictionary<DefaultMailCategory, int> { [DefaultMailCategory.Travel] = 1 };

        // Act
        var value = JsonSerializer.Serialize(DefaultMailCategory.Finance);
        var property = JsonSerializer.Serialize(categories);
        var readValue = JsonSerializer.Deserialize<DefaultMailCategory>(value);
        var readProperty = JsonSerializer.Deserialize<Dictionary<DefaultMailCategory, int>>(property);

        // Assert
        Assert.Equal("\"finance\"", value);
        Assert.Equal("""{"travel":1}""", property);
        Assert.Equal(DefaultMailCategory.Finance, readValue);
        Assert.Equal(1, readProperty![DefaultMailCategory.Travel]);
    }

    [Theory]
    [InlineData("\"invoices\"")]
    [InlineData("1")]
    public void Json_AnUndeclaredIdentityOrAWrongToken_IsRefused(string json)
    {
        // Act
        var refusal = Record.Exception(() => JsonSerializer.Deserialize<DefaultMailCategory>(json));

        // Assert
        Assert.IsType<JsonException>(refusal);
    }

    [Fact]
    public void Json_TheUnspecifiedDefault_IsNotWritten()
    {
        // Act
        var refusal = Record.Exception(() => JsonSerializer.Serialize(default(DefaultMailCategory)));

        // Assert
        Assert.IsType<JsonException>(refusal);
    }
}
