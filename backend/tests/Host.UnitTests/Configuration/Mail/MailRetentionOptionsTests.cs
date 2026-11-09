// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Host.Configuration.Mail;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail;

/// <summary>Covers the schedule the nightly retention walk is dispatched on.</summary>
public sealed class MailRetentionOptionsTests
{
    [Fact]
    public void Schedule_NothingConfigured_IsTwoInTheMorningAndValid()
    {
        // Arrange
        var options = new MailRetentionOptions();

        // Act
        var errors = options.FindConfigurationErrors().ToArray();

        // Assert
        Assert.Empty(errors);
        Assert.Equal("Daily at 02:00", options.Schedule);
        Assert.Equal("Daily at 02:00", options.ToRecurrence().Declaration);
    }

    /// <summary>Refused at start, because a schedule nothing can read would leave the walk never dispatched and nothing saying so.</summary>
    [Fact]
    public void FindConfigurationErrors_AScheduleThatNamesNoRecurrence_FailsNamingTheKey()
    {
        // Arrange
        var options = new MailRetentionOptions { Schedule = "whenever" };

        // Act
        var errors = options.FindConfigurationErrors().ToArray();

        // Assert
        var error = Assert.Single(errors);

        Assert.Contains("MailSynchronization:Retention:Schedule", error, StringComparison.Ordinal);
    }

    /// <summary>The fault reaches start-up through the section's own validation.</summary>
    [Fact]
    public void ValidateForSynchronization_AnUnreadableRetentionSchedule_IsReported()
    {
        // Arrange
        var options = new MailSynchronizationOptions { Retention = new MailRetentionOptions { Schedule = "whenever" } };

        // Act
        var results = options.ValidateForSynchronization().ToArray();

        // Assert
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(MailSynchronizationOptions.Retention)));
    }

    [Fact]
    public void ToRecurrence_AnUnvalidatedDeclaration_IsRefused()
    {
        // Arrange
        var options = new MailRetentionOptions { Schedule = "whenever" };

        // Act, Assert
        Assert.Throws<InvalidOperationException>(() => options.ToRecurrence());
    }
}
