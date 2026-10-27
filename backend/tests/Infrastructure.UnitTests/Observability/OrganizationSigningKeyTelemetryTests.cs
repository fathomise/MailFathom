// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers the counters that tell an operator their organizations, and their mail, are going unsigned.</summary>
public sealed class OrganizationSigningKeyTelemetryTests
{
    private const string UnmintedInstrument = "mailfathom.organization.signing_key.unminted";

    private const string UnsignedInstrument = "mailfathom.organization.mail.unsigned";

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

    [Fact]
    public void MessageSentUnsigned_EachMessageLeftUnsigned_CountsOneWithNoDimension()
    {
        // Arrange
        var telemetry = new OrganizationSigningKeyTelemetry();
        using var measurements = new RecordedMailFathomMeasurements(UnsignedInstrument);

        // Act
        telemetry.MessageSentUnsigned();
        telemetry.MessageSentUnsigned();

        // Assert
        Assert.Equal([1d, 1d], measurements.ValuesOf(UnsignedInstrument));
        Assert.All(measurements.Recorded, measurement => Assert.Empty(measurement.Tags));
    }
}
