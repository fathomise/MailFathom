// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text;
using MailFathom.Host.Configuration.Rules;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Rules;

/// <summary>Covers the limits the rule section still declares, and that the rules it once declared no longer stop a start.</summary>
public sealed class MailRulesOptionsTests
{
    /// <summary>The retired key is recognized rather than bound, so a deployment upgraded with it still starts.</summary>
    [Fact]
    public void Bind_ASectionStillDeclaringRules_BindsStrictlyAndAppliesNone()
    {
        // Act
        var bound = BindRules(
            """{ "MailRules": { "Rules": [ { "Name": "archive-receipts", "Condition": "isSeen", "Accounts": ["work"], "NoSuchKey": 1 } ] } }""");

        // Assert
        Assert.NotNull(bound.Rules);
        Assert.Empty(bound.FindConfigurationErrors());
    }

    [Fact]
    public void Bind_AMisspelledLimit_IsRefusedByTheStrictBinder()
    {
        // Act, Assert
        Assert.Throws<InvalidOperationException>(() => BindRules("""{ "MailRules": { "MaxConditionLenght": 10 } }"""));
    }

    [Fact]
    public void FindConfigurationErrors_ALimitOutOfRange_NamesTheKey()
    {
        // Arrange
        var options = new MailRulesOptions { MaxConditionLength = 0 };

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("MailRules:MaxConditionLength — ", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_ATimeoutPastTheCeiling_IsRefused()
    {
        // Arrange
        var options = new MailRulesOptions { ConditionEvaluationTimeout = TimeSpan.FromMinutes(1) };

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("MailRules:ConditionEvaluationTimeout — ", error, StringComparison.Ordinal);
    }

    private static MailRulesOptions BindRules(string document)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(document));

        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();

        return configuration
            .GetSection(MailRulesOptions.SectionName)
            .Get<MailRulesOptions>(binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)!;
    }
}
