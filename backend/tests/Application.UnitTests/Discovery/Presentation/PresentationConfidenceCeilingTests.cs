// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Discovery.Presentation;
using Xunit;

namespace MailFathom.Application.UnitTests.Discovery.Presentation;

/// <summary>Covers how far the support beneath an answer lets the band its producer reported stand.</summary>
public sealed class PresentationConfidenceCeilingTests
{
    /// <summary>A band over sources that back the answer stands as reported, and so does one over sources nothing checked.</summary>
    [Theory]
    [InlineData(PresentationSupport.Supported, PresentationConfidence.High, PresentationConfidence.High)]
    [InlineData(PresentationSupport.Cited, PresentationConfidence.High, PresentationConfidence.High)]
    [InlineData(PresentationSupport.Cited, PresentationConfidence.Low, PresentationConfidence.Low)]
    [InlineData(PresentationSupport.Conflicting, PresentationConfidence.High, PresentationConfidence.Moderate)]
    [InlineData(PresentationSupport.Stale, PresentationConfidence.High, PresentationConfidence.Moderate)]
    [InlineData(PresentationSupport.Stale, PresentationConfidence.Low, PresentationConfidence.Low)]
    [InlineData(PresentationSupport.Unsupported, PresentationConfidence.High, PresentationConfidence.Low)]
    public void Cap_AReportedBand_StandsAsFarAsTheSupportCarriesIt(
        PresentationSupport support,
        PresentationConfidence reported,
        PresentationConfidence expected)
    {
        // Act
        var capped = PresentationConfidenceCeiling.Cap(reported, support);

        // Assert
        Assert.Equal(expected, capped);
    }

    /// <summary>A word naming a band is read as that band, and one naming none never reads more confident than moderate.</summary>
    [Theory]
    [InlineData(" high ", PresentationConfidence.High)]
    [InlineData("LOW", PresentationConfidence.Low)]
    [InlineData("moderate", PresentationConfidence.Moderate)]
    [InlineData("certain", PresentationConfidence.Moderate)]
    [InlineData(null, PresentationConfidence.Moderate)]
    public void Cap_AReportedWord_IsReadAsTheBandItNames(string? reported, PresentationConfidence expected)
    {
        // Act
        var capped = PresentationConfidenceCeiling.Cap(reported, PresentationSupport.Supported);

        // Assert
        Assert.Equal(expected, capped);
    }
}
