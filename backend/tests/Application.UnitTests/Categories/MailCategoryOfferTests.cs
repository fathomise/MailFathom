// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Categories;
using MailFathom.Domain.Categories;
using Xunit;

namespace MailFathom.Application.UnitTests.Categories;

/// <summary>Covers which names one account offers, and the two ways a name is compared against them.</summary>
public sealed class MailCategoryOfferTests
{
    [Fact]
    public void Of_NothingSwitchedOff_OffersEveryDefaultAndEveryOwnCategory()
    {
        // Act
        var offer = MailCategoryOffer.Of([], ["Contracts"]);

        // Assert
        Assert.Equal([.. DefaultMailCategory.All.Select(static category => category.ToString()), "Contracts"], offer.Names);
    }

    [Fact]
    public void Of_ASwitchedOffDefault_LeavesItOut()
    {
        // Act
        var offer = MailCategoryOffer.Of(["finance"], []);

        // Assert
        Assert.DoesNotContain("finance", offer.Names);
    }

    [Fact]
    public void NameMatching_AnotherCase_AnswersTheStoredSpelling()
    {
        // Act
        var name = MailCategoryOffer.Of([], ["Contracts"]).NameMatching("CONTRACTS");

        // Assert
        Assert.Equal("Contracts", name);
    }

    [Fact]
    public void NameMatching_ANameNothingOffers_AnswersNothing()
    {
        // Act
        var name = MailCategoryOffer.Of([], []).NameMatching("Contracts");

        // Assert
        Assert.Null(name);
    }

    /// <summary>A record is exact where a request is forgiving, because a rename in case is a withdrawal of the old name.</summary>
    [Fact]
    public void Shows_ARecordDifferingOnlyInCase_IsNotShown()
    {
        // Act
        var shown = MailCategoryOffer.Of([], ["Contracts"]).Shows("contracts");

        // Assert
        Assert.False(shown);
    }

    [Fact]
    public void None_OffersNothing()
    {
        // Assert
        Assert.Empty(MailCategoryOffer.None.Names);
    }
}
