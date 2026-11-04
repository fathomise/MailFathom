// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Documents;
using UglyToad.PdfPig.Tokens;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Documents;

/// <summary>Covers which of the PDF library's filters may stand without a charge in front of them.</summary>
public sealed class BoundedPdfFilterProviderTests
{
    /// <summary>
    /// The image filters are passed through uncharged only because the library decodes none of them, so a release that
    /// starts decoding one has to fail here rather than open a decoder nothing bounds.
    /// </summary>
    [Fact]
    public void GetNamedFilters_TheImageFiltersPassedThroughUncharged_AreOnesTheLibraryDoesNotDecode()
    {
        // Arrange
        var provider = new BoundedPdfFilterProvider(new PdfPageDecodeBudget(1024));

        // Act
        var filters = provider.GetNamedFilters(
            [NameToken.DctDecode, NameToken.DctDecodeAbbreviation, NameToken.JpxDecode, NameToken.Jbig2Decode]);

        // Assert
        Assert.All(filters, filter => Assert.False(filter.IsSupported));
    }
}
