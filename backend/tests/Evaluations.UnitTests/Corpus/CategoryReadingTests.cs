// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Evaluations.Corpus;
using Xunit;

namespace MailFathom.Evaluations.UnitTests.Corpus;

/// <summary>Proves the category check fails on exactly the runs it exists to catch.</summary>
public sealed class CategoryReadingTests
{
    public static TheoryData<string, string[], string?, bool> Runs { get; } = new()
    {
        { "narrows", ["FINANCE"], null, true },
        { "narrows", [], null, false },
        { "never", [], null, true },
        { "never", ["finance"], null, false },
        { "names", [], "It was sorted into Subscriptions.", true },
        { "names", [], "It claims to be finance mail.", false },
        { "names", [], null, false },
    };

    [Theory]
    [MemberData(nameof(Runs))]
    public void Shortfall_ARun_IsReportedOnlyWhereItReadTheCategoryWrongly(string reading, string[] narrowedBy, string? answer, bool expected)
    {
        // Arrange
        var category = reading switch
        {
            "narrows" => CategoryReading.NarrowsBy("finance"),
            "never" => CategoryReading.NeverNarrows,
            _ => CategoryReading.Names("subscriptions"),
        };

        // Act
        var shortfall = category.Shortfall(narrowedBy, answer);

        // Assert
        Assert.Equal(expected, shortfall is null);
    }
}
