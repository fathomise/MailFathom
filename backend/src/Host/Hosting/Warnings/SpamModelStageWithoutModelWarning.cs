// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Spam.Model;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Spam;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Infrastructure.Persistence.Users.AccountSettings;

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
/// The accounts are the served records themselves, read whole, because a mailbox is a user's own record and the
/// published settings carry no user. That read grows with the deployment, so it is made only where no model serves the
/// stage — a deployment that declares one asks the judge and stops — and it runs behind the schema gate, since the
/// records live in a table that migration creates.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class SpamModelStageWithoutModelWarning : IHostedService
{
    private readonly ISpamModelJudge judge;
    private readonly IServedMailAccountReader servedAccounts;
    private readonly ILogger<SpamModelStageWithoutModelWarning> logger;

    /// <summary>Initializes a new startup warning.</summary>
    /// <param name="judge">Answers whether a model serves the stage, in which case nothing is read.</param>
    /// <param name="servedAccounts">Reads the account records this deployment serves.</param>
    /// <param name="logger">The startup logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public SpamModelStageWithoutModelWarning(
        ISpamModelJudge judge,
        IServedMailAccountReader servedAccounts,
        ILogger<SpamModelStageWithoutModelWarning> logger)
    {
        ArgumentNullException.ThrowIfNull(judge);
        ArgumentNullException.ThrowIfNull(servedAccounts);
        ArgumentNullException.ThrowIfNull(logger);

        this.judge = judge;
        this.servedAccounts = servedAccounts;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (this.judge.IsActive)
        {
            return;
        }

        var askingAccounts = (await this.servedAccounts.ReadServedRecordsAsync(cancellationToken))
            .Select(MailAccountQueryableSettingsReading.TryBind)
            .OfType<MailSynchronizationAccountOptions>()
            .Where(static account => account.SpamClassification is { Enabled: true, ModelStage.Enabled: true })
            .Select(static account => account.AccountId);

        foreach (var accountId in askingAccounts)
        {
            this.LogModelStageWithoutModel(accountId);
        }
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
