// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Host.Configuration.Rules;

namespace MailFathom.Host.Hosting.Warnings;

/// <summary>States at startup that the configuration still declares rules, which are no longer read from it.</summary>
/// <remarks>
/// <para>
/// A report rather than a refusal, which is what
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0048-keeping-a-mail-accounts-rules-in-postgresql-and-changing-them-at-runtime.md">ADR 0048</see>
/// decides: a mail account's rules are kept in PostgreSQL, and a deployment upgraded with the section still in its
/// file has to start so its operator can import them.
/// </para>
/// <para>
/// The warning carries a count and nothing else. A rule's name and its condition are authored text, which can name a
/// person or a correspondent, so neither reaches a log.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class ConfiguredMailRulesIgnoredWarning : IHostedService
{
    private readonly IConfiguration configuration;
    private readonly ILogger<ConfiguredMailRulesIgnoredWarning> logger;

    /// <summary>Initializes a new startup warning.</summary>
    /// <param name="configuration">The configuration the deployment started with.</param>
    /// <param name="logger">The startup logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    public ConfiguredMailRulesIgnoredWarning(
        IConfiguration configuration,
        ILogger<ConfiguredMailRulesIgnoredWarning> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        this.configuration = configuration;
        this.logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var declaredRuleCount = this.configuration
            .GetSection($"{MailRulesOptions.SectionName}:{nameof(MailRulesOptions.Rules)}")
            .GetChildren()
            .Count();

        if (declaredRuleCount > 0)
        {
            this.LogConfiguredRulesIgnored(declaredRuleCount);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "MailRules:Rules declares {DeclaredRuleCount} rules, and none of them is applied: a mail account's rules "
            + "are now stored per account in the database. Import them with 'mfctl rules import --account', review them, "
            + "and enable them, then remove the section from your configuration.")]
    private partial void LogConfiguredRulesIgnored(int declaredRuleCount);
}
