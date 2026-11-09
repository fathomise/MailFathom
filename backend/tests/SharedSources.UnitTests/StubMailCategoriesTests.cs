// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Categories;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the double several suites build a mailbox read's categories from.</summary>
public sealed class StubMailCategoriesTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("primary");

    [Fact]
    public void OfferOf_AnAccountItWasGiven_AnswersThatOffer()
    {
        // Arrange
        var offer = MailCategoryOffer.Of([], []);
        var categories = new StubMailCategories(
            new Dictionary<MailAccountId, MailCategoryOffer> { [Account] = offer },
            new Dictionary<StoredEmailId, IReadOnlyList<string>>());

        // Act
        var answered = categories.OfferOf(Account);

        // Assert
        Assert.Same(offer, answered);
    }

    [Fact]
    public void OfferOf_AnyOtherAccount_OffersNothing()
    {
        // Act
        var answered = new StubMailCategories().OfferOf(Account);

        // Assert
        Assert.Same(MailCategoryOffer.None, answered);
    }

    [Fact]
    public async Task ReadAsync_AnyMessages_AnswersWhatItWasGiven()
    {
        // Arrange
        var email = StoredEmailId.Create(Guid.CreateVersion7());
        var categories = new StubMailCategories(
            new Dictionary<MailAccountId, MailCategoryOffer>(),
            new Dictionary<StoredEmailId, IReadOnlyList<string>> { [email] = ["finance"] });

        // Act
        var recorded = await categories.ReadAsync([email], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["finance"], recorded[email]);
    }
}
