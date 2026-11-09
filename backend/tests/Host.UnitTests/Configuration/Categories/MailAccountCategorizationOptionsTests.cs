// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Categories;
using MailFathom.Host.Configuration.Categories;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Categories;

/// <summary>Covers what an account's categorization block may say.</summary>
public sealed class MailAccountCategorizationOptionsTests
{
    /// <summary>Nothing new leaves the deployment until an account asks for it.</summary>
    [Fact]
    public void NewBlock_WritesNothing_IsOffAndRefusesNothing()
    {
        // Arrange, Act
        var options = new MailAccountCategorizationOptions();

        // Assert
        Assert.False(options.Enabled);
        Assert.Empty(options.FindRefusals());
    }

    [Fact]
    public void FindRefusals_ADisabledDefaultNothingDeclares_NamesItsPositionAndWhatItTakesWithoutRepeatingIt()
    {
        // Arrange
        var options = new MailAccountCategorizationOptions { DisabledDefaults = ["finance", "a stray note"] };

        // Act
        var refusal = Assert.Single(options.FindRefusals());

        // Assert
        Assert.Contains("DisabledDefaults:1", refusal.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("correspondence", refusal.ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("a stray note", refusal.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void FindRefusals_ADisabledDefaultNamedTwice_IsRefused()
    {
        // Arrange
        var options = new MailAccountCategorizationOptions { DisabledDefaults = ["finance", "finance"] };

        // Act
        var refusal = Assert.Single(options.FindRefusals());

        // Assert
        Assert.Contains("DisabledDefaults:1", refusal.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void FindRefusals_AsManyOwnCategoriesAsAnAccountHolds_RefusesNothing()
    {
        // Arrange
        var options = HoldingOwnCategories(OwnMailCategoryBounds.MaximumPerAccount);

        // Act
        var refusals = options.FindRefusals();

        // Assert
        Assert.Empty(refusals);
    }

    [Fact]
    public void FindRefusals_OneOwnCategoryPastTheLimit_IsRefused()
    {
        // Arrange
        var options = HoldingOwnCategories(OwnMailCategoryBounds.MaximumPerAccount + 1);

        // Act
        var refusal = Assert.Single(options.FindRefusals());

        // Assert
        Assert.Equal([nameof(MailAccountCategorizationOptions.OwnCategories)], refusal.MemberNames);
    }

    /// <summary>A refusal is written to a log, and a subject somebody named can be personal data.</summary>
    [Theory]
    [InlineData("", "A description.", "Name", "is empty")]
    [InlineData("   ", "A description.", "Name", "is empty")]
    [InlineData(" Padded", "A description.", "Name", "white space")]
    [InlineData("Line\nbreak", "A description.", "Name", "control character")]
    [InlineData("Contracts", "", "Description", "is empty")]
    [InlineData("Contracts", "Line\nbreak", "Description", "control character")]
    public void FindRefusals_AnUnusableOwnCategory_NamesTheKeyByPositionAndWhy(
        string name,
        string description,
        string key,
        string reason)
    {
        // Arrange
        var options = new MailAccountCategorizationOptions { OwnCategories = [Own(name, description)] };

        // Act
        var refusal = Assert.Single(options.FindRefusals());

        // Assert
        Assert.Contains($"Categorization:OwnCategories:0:{key}", refusal.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains(reason, refusal.ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("Padded", refusal.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void FindRefusals_ANameAtItsBound_RefusesNothingAndOnePastItIsRefused()
    {
        // Arrange
        var atBound = new MailAccountCategorizationOptions
        {
            OwnCategories = [Own(new string('n', OwnMailCategoryBounds.MaximumNameLength), new string('d', OwnMailCategoryBounds.MaximumDescriptionLength))],
        };
        var pastBound = new MailAccountCategorizationOptions
        {
            OwnCategories = [Own(new string('n', OwnMailCategoryBounds.MaximumNameLength + 1), new string('d', OwnMailCategoryBounds.MaximumDescriptionLength + 1))],
        };

        // Act
        var accepted = atBound.FindRefusals();
        var refused = pastBound.FindRefusals().ToArray();

        // Assert
        Assert.Empty(accepted);
        Assert.Equal(2, refused.Length);
    }

    [Fact]
    public void FindRefusals_TwoOwnCategoriesDifferingOnlyInCase_AreRefused()
    {
        // Arrange
        var options = new MailAccountCategorizationOptions
        {
            OwnCategories = [Own("Contracts", "One."), Own("contracts", "Two.")],
        };

        // Act
        var refusal = Assert.Single(options.FindRefusals());

        // Assert
        Assert.Contains("OwnCategories:1", refusal.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>A search and an agent are offered both kinds under one set of names, so a name has to mean one thing.</summary>
    [Fact]
    public void FindRefusals_AnOwnCategoryTakingADefaultsName_IsRefusedWhateverTheCase()
    {
        // Arrange
        var options = new MailAccountCategorizationOptions { OwnCategories = [Own("Finance", "Our books.")] };

        // Act
        var refusal = Assert.Single(options.FindRefusals());

        // Assert
        Assert.Contains("OwnCategories:0", refusal.ErrorMessage, StringComparison.Ordinal);
    }

    private static MailAccountCategorizationOptions HoldingOwnCategories(int count) => new()
    {
        OwnCategories = [.. Enumerable.Range(0, count).Select(index => Own($"Subject {index}", "A description."))],
    };

    private static MailAccountOwnCategoryOptions Own(string name, string description) =>
        new() { Name = name, Description = description };
}
