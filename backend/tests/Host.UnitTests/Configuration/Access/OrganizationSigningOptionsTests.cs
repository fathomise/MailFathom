// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using MailFathom.Application.Access.Organizations.SigningKeys;
using MailFathom.Host.Configuration.Access;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Access;

/// <summary>Covers the interval an organization's signing key is rotated on, which is refused below a day.</summary>
public sealed class OrganizationSigningOptionsTests
{
    [Fact]
    public void Validate_TheDefaults_AreAcceptedAndRotateYearly()
    {
        // Arrange
        var options = new OrganizationSigningOptions();

        // Act
        var results = Validate(options);

        // Assert
        Assert.Empty(results);
        Assert.Equal(TimeSpan.FromDays(365), options.ToSigningSettings().KeyRotationInterval);
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("23:59:59")]
    public void Validate_AnIntervalShorterThanADay_IsRefusedNamingTheKey(string interval)
    {
        // Arrange
        var options = new OrganizationSigningOptions { KeyRotationInterval = TimeSpan.Parse(interval, CultureInfo.InvariantCulture) };

        // Act
        var results = Validate(options);

        // Assert
        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(OrganizationSigningOptions.KeyRotationInterval), StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("1.00:00:00")]
    [InlineData("3650.00:00:00")]
    public void Validate_AnIntervalOfADayOrLonger_IsAccepted(string interval)
    {
        // Arrange
        var options = new OrganizationSigningOptions { KeyRotationInterval = TimeSpan.Parse(interval, CultureInfo.InvariantCulture) };

        // Act
        var results = Validate(options);

        // Assert
        Assert.Empty(results);
        Assert.Equal(options.KeyRotationInterval, options.ToSigningSettings().KeyRotationInterval);
    }

    /// <summary>The pass subtracts the interval from the current time, so a value it could not subtract never reaches it.</summary>
    [Fact]
    public void Validate_AnIntervalPastTheCeiling_IsRefusedNamingTheKey()
    {
        // Arrange
        var options = new OrganizationSigningOptions
        {
            KeyRotationInterval = OrganizationSigningSettings.MaximumKeyRotationInterval + TimeSpan.FromTicks(1),
        };

        // Act
        var results = Validate(options);

        // Assert
        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(OrganizationSigningOptions.KeyRotationInterval), StringComparer.Ordinal));
    }

    [Fact]
    public void KeyRotationInterval_TheCeilingTheOptionsEnforce_IsTheOneTheApplicationStates()
    {
        // Arrange
        var options = new OrganizationSigningOptions { KeyRotationInterval = OrganizationSigningSettings.MaximumKeyRotationInterval };

        // Act
        var results = Validate(options);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void KeyRotationInterval_TheFloorTheOptionsEnforce_IsTheOneTheApplicationStates()
    {
        // Arrange
        var options = new OrganizationSigningOptions { KeyRotationInterval = OrganizationSigningSettings.MinimumKeyRotationInterval };

        // Act
        var results = Validate(options);

        // Assert
        Assert.Empty(results);
    }

    private static List<ValidationResult> Validate(OrganizationSigningOptions options)
    {
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        return results;
    }
}
