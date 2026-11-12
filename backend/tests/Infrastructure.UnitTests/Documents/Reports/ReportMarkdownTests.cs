// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Documents.Reports;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Documents.Reports;

/// <summary>Covers which citation markers of a report's body are carried through the parse as citations.</summary>
public sealed class ReportMarkdownTests
{
    /// <summary>A marker naming one of the sources is carried; one naming a source the report does not list stays as written.</summary>
    [Fact]
    public void WithCitationsCarried_AMarkerInsideAndOnePastTheSources_CarriesOnlyTheOneInside()
    {
        // Act
        var carried = ReportMarkdown.WithCitationsCarried("a[^2] b[^3] c[^0]", sourceCount: 2);

        // Assert
        Assert.Equal("a2 b[^3] c[^0]", carried);
    }

    /// <summary>The characters a carried citation is written in are removed from the body first, so a body cannot forge a citation of its own.</summary>
    [Fact]
    public void WithCitationsCarried_ABodyHoldingTheCarriedFormAlready_LosesIt()
    {
        // Act
        var carried = ReportMarkdown.WithCitationsCarried("forged 1", sourceCount: 1);

        // Assert
        Assert.Equal("forged 1", carried);
    }
}
