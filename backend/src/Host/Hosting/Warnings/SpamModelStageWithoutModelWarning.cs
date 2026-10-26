// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Spam;

namespace MailFathom.Host.Hosting.Warnings;

/// <summary>States at startup which accounts ask for the spam model stage on a deployment that declares no model for it.</summary>
/// <remarks>
/// <para>
/// A report rather than a refusal. A record switching the stage on is refused when it is written to a deployment with no
/// decision model for spam classification, so a record reaching here asked for it before that model was taken away.
/// Refusing the start over it would refuse every user's mail over one account's switch, while classifying that account
/// deterministically is what the stage would have left anyway had its model been unreachable.
/// </para>
/// <para>
/// Registered only where no decision model serves spam classification, so a deployment that declares one pays nothing
/// here.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class SpamModelStageWithoutModelWarning : IHostedService
{
    private readonly ISettingsSnapshot<MailSynchronizationOptions> mailSettings;
    private readonly ILogger<SpamModelStageWithoutModelWarning> logger;

    /// <summary>Initializes a new startup warning.</summary>
    /// <param name="mailSettings">The published account roster, which is the one in force when this runs.</param>
    /// <param name="logger">The startup logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="mailSettings" /> is <see langword="null" />.</exception>
    public SpamModelStageWithoutModelWarning(
        ISettingsSnapshot<MailSynchronizationOptions> mailSettings,
        ILogger<SpamModelStageWithoutModelWarning> logger)
    {
        ArgumentNullException.ThrowIfNull(mailSettings);

        this.mailSettings = mailSettings;
        this.logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var askingAccounts = this.mailSettings.Current.DeclaredAccounts
            .Where(static account => account.SpamClassification is { Enabled: true, ModelStage.Enabled: true })
            .Select(static account => account.AccountId);

        foreach (var accountId in askingAccounts)
        {
            this.LogModelStageWithoutModel(accountId);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Account {AccountId} switches " + MailAccountSpamModelStageOptions.EnabledPath + " on, but this deployment "
            + "declares no decision model under Decisions:Models for Decisions:SpamClassification:Model, so its mail is "
            + "classified by the deterministic stage alone.")]
    private partial void LogModelStageWithoutModel(string? accountId);
}
