// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers the counter that tells an operator their organizations are going unsigned for want of a key ring.</summary>
public sealed class OrganizationSigningKeyTelemetryTests
{
    private const string UnmintedInstrument = "mailfathom.organization.signing_key.unminted";

    [Fact]
    public void MintRefusedWithoutKeyRing_EachOrganizationLeftWithoutAKey_CountsOneWithNoDimension()
    {
        // Arrange
        var telemetry = new OrganizationSigningKeyTelemetry();
        using var measurements = new RecordedMailFathomMeasurements(UnmintedInstrument);

        // Act
        telemetry.MintRefusedWithoutKeyRing();
        telemetry.MintRefusedWithoutKeyRing();

        // Assert
        Assert.Equal([1d, 1d], measurements.ValuesOf(UnmintedInstrument));
        Assert.All(measurements.Recorded, measurement => Assert.Empty(measurement.Tags));
    }
}
