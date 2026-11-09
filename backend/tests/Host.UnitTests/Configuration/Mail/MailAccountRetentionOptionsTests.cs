// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Host.Configuration.Mail;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail;

public sealed class MailAccountRetentionOptionsTests
{
    [Fact]
    public void Defaults_AnAccountStatingNothing_HasBothHalvesOffAtTheirDefaultAges()
    {
        // Act
        var retention = new MailAccountRetentionOptions();

        // Assert
        Assert.False(retention.Messages.Enabled);
        Assert.Equal(TimeSpan.FromDays(730), retention.Messages.After);
        Assert.False(retention.Tombstones.Enabled);
        Assert.Equal(TimeSpan.FromDays(30), retention.Tombstones.After);
        Assert.Empty(retention.ExcludedFolders);
        Assert.Empty(retention.FindRefusals());
    }

    [Theory]
    [InlineData(30 * 24 - 1)]
    [InlineData(0)]
    [InlineData(-1)]
    public void FindRefusals_AMessageAgeBelowItsFloor_NamesTheKeyAndTheFloor(int ageHours)
    {
        // Arrange
        var retention = new MailAccountRetentionOptions();
        retention.Messages.After = TimeSpan.FromHours(ageHours);

        // Act
        var refusal = Assert.Single(retention.FindRefusals());

        // Assert
        Assert.Equal("Retention:Messages:After must be at least 30.00:00:00.", refusal);
    }

    [Theory]
    [InlineData(23)]
    [InlineData(0)]
    public void FindRefusals_ATombstoneAgeBelowItsFloor_NamesTheKeyAndTheFloor(int ageHours)
    {
        // Arrange
        var retention = new MailAccountRetentionOptions();
        retention.Tombstones.After = TimeSpan.FromHours(ageHours);

        // Act
        var refusal = Assert.Single(retention.FindRefusals());

        // Assert
        Assert.Equal("Retention:Tombstones:After must be at least 1.00:00:00.", refusal);
    }

    /// <summary>The floor is the youngest age accepted, and a long age endangers nothing, so nothing caps it.</summary>
    [Fact]
    public void FindRefusals_AgesAtTheirFloorsAndAtTheLongestAge_RefusesNothing()
    {
        // Arrange
        var atFloors = new MailAccountRetentionOptions();
        atFloors.Messages.After = MailAccountRetentionOptions.MinimumMessageAge;
        atFloors.Tombstones.After = MailAccountRetentionOptions.MinimumTombstoneAge;
        var atLongest = new MailAccountRetentionOptions();
        atLongest.Messages.After = TimeSpan.MaxValue;
        atLongest.Tombstones.After = TimeSpan.MaxValue;

        // Act
        var refusals = atFloors.FindRefusals().Concat(atLongest.FindRefusals());

        // Assert
        Assert.Empty(refusals);
    }

    [Fact]
    public void FindRefusals_AHalfWithNoBlock_ReportsIt()
    {
        // Arrange
        var retention = new MailAccountRetentionOptions { Tombstones = null! };

        // Act
        var refusal = Assert.Single(retention.FindRefusals());

        // Assert
        Assert.Equal("Retention:Tombstones must be a block.", refusal);
    }

    [Fact]
    public void FindRefusals_NoExclusionList_ReportsIt()
    {
        // Arrange
        var retention = new MailAccountRetentionOptions { ExcludedFolders = null! };

        // Act
        var refusal = Assert.Single(retention.FindRefusals());

        // Assert
        Assert.Equal("Retention:ExcludedFolders must be a list.", refusal);
    }

    [Fact]
    public void FindRefusals_ExclusionsAtTheirCountBound_RefusesNothing()
    {
        // Arrange
        var retention = new MailAccountRetentionOptions
        {
            ExcludedFolders = [.. Enumerable.Repeat("Archive", MailAccountRetentionOptions.MaximumExcludedFolders)],
        };

        // Act
        var refusals = retention.FindRefusals();

        // Assert
        Assert.Empty(refusals);
    }

    [Fact]
    public void FindRefusals_ExclusionsPastTheirCountBound_NamesTheBound()
    {
        // Arrange
        var retention = new MailAccountRetentionOptions
        {
            ExcludedFolders = [.. Enumerable.Repeat("Archive", MailAccountRetentionOptions.MaximumExcludedFolders + 1)],
        };

        // Act
        var refusal = Assert.Single(retention.FindRefusals());

        // Assert
        Assert.Equal("Retention:ExcludedFolders holds more than 256 entries.", refusal);
    }

    /// <summary>A policy states one list for many accounts, so naming a folder this account lacks is no fault of the list.</summary>
    [Theory]
    [InlineData("A folder nobody has")]
    [InlineData("0199a4f2-7c1e-7d3a-9b1f-2c4e6a8d0f12")]
    public void FindRefusals_AnEntryNamingNoFolderOfTheAccount_IsNotRefused(string entry)
    {
        // Arrange
        var retention = new MailAccountRetentionOptions { ExcludedFolders = [entry] };

        // Act
        var refusals = retention.FindRefusals();

        // Assert
        Assert.Empty(refusals);
    }

    [Fact]
    public void FindRefusals_AnEntryAtTheLengthBound_IsNotRefused()
    {
        // Arrange
        var retention = new MailAccountRetentionOptions
        {
            ExcludedFolders = [new string('a', MailAccountRetentionOptions.MaximumExcludedFolderLength)],
        };

        // Act
        var refusals = retention.FindRefusals();

        // Assert
        Assert.Empty(refusals);
    }

    /// <summary>An alias can name a person or a matter, so a refusal points at the entry and never repeats it.</summary>
    [Theory]
    [InlineData("  ")]
    [InlineData("Private\nmatter")]
    [InlineData(null)]
    public void FindRefusals_AnEntryThatIsNoFolderName_NamesItsPositionAndNotWhatItHolds(string? entry)
    {
        // Arrange
        var retention = new MailAccountRetentionOptions { ExcludedFolders = ["Archive", entry!] };

        // Act
        var refusal = Assert.Single(retention.FindRefusals());

        // Assert
        Assert.StartsWith("Retention:ExcludedFolders entry 1 must be", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("matter", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void FindRefusals_AnEntryPastTheLengthBound_NamesItsPositionAndNotWhatItHolds()
    {
        // Arrange
        var retention = new MailAccountRetentionOptions
        {
            ExcludedFolders = [new string('a', MailAccountRetentionOptions.MaximumExcludedFolderLength + 1)],
        };

        // Act
        var refusal = Assert.Single(retention.FindRefusals());

        // Assert
        Assert.StartsWith("Retention:ExcludedFolders entry 0 must be", refusal, StringComparison.Ordinal);
        Assert.Contains("at most 128 characters", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("aaa", refusal, StringComparison.Ordinal);
    }
}
