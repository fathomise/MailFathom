// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Categories;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Categories;

/// <summary>Covers what an account's offer does to a read: what may be asked for, and what may be shown.</summary>
public sealed class OfferedMailCategoriesTests
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    private static readonly MailAccountId Work = MailAccountId.Create("work");

    private static readonly MailAccountId Home = MailAccountId.Create("home");

    private static MailboxScope BothAccounts { get; } = MailboxScope.Create([Work, Home], []);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SelectionWithin_NoCategoryNamed_SelectsNothing(string? category)
    {
        // Act
        var selection = CategoriesOffering().SelectionWithin(BothAccounts, category);

        // Assert
        Assert.Null(selection);
    }

    [Fact]
    public void SelectionWithin_ANameWrittenInAnotherCase_ReadsTheStoredSpelling()
    {
        // Arrange
        var categories = CategoriesOffering((Work, MailCategoryOffer.Of([], ["Contracts"])));

        // Act
        var selection = categories.SelectionWithin(BothAccounts, " contracts ");

        // Assert
        Assert.NotNull(selection);
        Assert.Equal(["Contracts"], selection.StoredNames);
        Assert.Equal("CONTRACTS", selection.ComparableName);
    }

    /// <summary>A category one account hides stays recorded, so the account has to leave the selection with it.</summary>
    [Fact]
    public void SelectionWithin_OneAccountSwitchedTheDefaultOff_SelectsOnlyTheAccountOfferingIt()
    {
        // Arrange
        var categories = CategoriesOffering(
            (Work, MailCategoryOffer.Of([], [])),
            (Home, MailCategoryOffer.Of(["finance"], [])));

        // Act
        var selection = categories.SelectionWithin(BothAccounts, "finance");

        // Assert
        Assert.NotNull(selection);
        Assert.Equal([Work], selection.Accounts);
    }

    [Fact]
    public void SelectionWithin_TheSameOwnNameSpelledTwoWays_KeepsBothSpellings()
    {
        // Arrange
        var categories = CategoriesOffering(
            (Work, MailCategoryOffer.Of([], ["Contracts"])),
            (Home, MailCategoryOffer.Of([], ["CONTRACTS"])));

        // Act
        var selection = categories.SelectionWithin(BothAccounts, "contracts");

        // Assert
        Assert.NotNull(selection);
        Assert.Equal(["CONTRACTS", "Contracts"], selection.StoredNames.Order(StringComparer.Ordinal));
        Assert.Equal([Home, Work], selection.Accounts.OrderBy(static account => account.Value, StringComparer.Ordinal));
    }

    [Fact]
    public void SelectionWithin_ASwitchedOffDefault_IsRefusedNamingWhatIsOffered()
    {
        // Arrange
        var offer = MailCategoryOffer.Of(["correspondence", "finance", "subscriptions", "shopping", "travel", "notifications"], ["Contracts"]);
        var categories = CategoriesOffering((Work, offer));

        // Act
        var refusal = Assert.Throws<MailboxQueryFilterInvalidException>(() => categories.SelectionWithin(BothAccounts, "finance"));

        // Assert
        Assert.Equal("category", refusal.FilterName);
        Assert.Contains("Contracts, other", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("finance", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectionWithin_NoAccountCategorizing_IsRefusedSayingNothingIsOffered()
    {
        // Act
        var refusal = Assert.Throws<MailboxQueryFilterInvalidException>(() => CategoriesOffering().SelectionWithin(BothAccounts, "finance"));

        // Assert
        Assert.Contains("no account in scope offers any", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithCategoriesAsync_ARecordedDefaultTheAccountSwitchedOff_IsWithheld()
    {
        // Arrange
        var summary = SummaryOf(Work);
        var categories = StubMailCategories.Offering(new StubMailCategories(
            new Dictionary<MailAccountId, MailCategoryOffer> { [Work] = MailCategoryOffer.Of(["finance"], ["Contracts"]) },
            new Dictionary<StoredEmailId, IReadOnlyList<string>> { [summary.StoredEmailId] = ["Contracts", "finance"] }));

        // Act
        var published = await categories.WithCategoriesAsync([summary], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["Contracts"], Assert.Single(published).Categories);
    }

    [Fact]
    public async Task WithCategoriesAsync_AnAccountThatStoppedCategorizing_PublishesNone()
    {
        // Arrange
        var summary = SummaryOf(Home);
        var categories = StubMailCategories.Offering(new StubMailCategories(
            new Dictionary<MailAccountId, MailCategoryOffer>(),
            new Dictionary<StoredEmailId, IReadOnlyList<string>> { [summary.StoredEmailId] = ["finance"] }));

        // Act
        var published = await categories.WithCategoriesAsync([summary], TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(Assert.Single(published).Categories);
    }

    [Fact]
    public async Task WithCategoriesAsync_ASummaryNothingCategorized_ComesBackInItsPlace()
    {
        // Arrange
        var categorized = SummaryOf(Work);
        var untouched = SummaryOf(Work);
        var categories = StubMailCategories.Offering(new StubMailCategories(
            new Dictionary<MailAccountId, MailCategoryOffer> { [Work] = MailCategoryOffer.Of([], []) },
            new Dictionary<StoredEmailId, IReadOnlyList<string>> { [categorized.StoredEmailId] = ["travel"] }));

        // Act
        var published = await categories.WithCategoriesAsync([untouched, categorized], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([untouched.StoredEmailId, categorized.StoredEmailId], published.Select(static summary => summary.StoredEmailId));
        Assert.Empty(published[0].Categories);
        Assert.Equal(["travel"], published[1].Categories);
    }

    private static OfferedMailCategories CategoriesOffering(params (MailAccountId Account, MailCategoryOffer Offer)[] offers) =>
        StubMailCategories.Offering(new StubMailCategories(
            offers.ToDictionary(static entry => entry.Account, static entry => entry.Offer),
            new Dictionary<StoredEmailId, IReadOnlyList<string>>()));

    private static EmailSummary SummaryOf(MailAccountId account) =>
        SyntheticEmailSummaries.Create(ReceivedAt, accountId: account.Value);
}
