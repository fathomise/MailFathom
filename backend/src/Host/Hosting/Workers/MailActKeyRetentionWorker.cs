// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Mail.Mutations.Authoring.Idempotency;

namespace MailFathom.Host.Hosting.Workers;

/// <summary>Deletes the keys people's mailbox acts were made under once their day has passed.</summary>
/// <remarks>
/// <para>
/// The keys are per person rather than per account, so one pass covers all of them and no account run is the place to
/// age them out. It takes no lease: every replica runs it, and a delete that finds a row already gone has nothing left
/// to do, so two replicas sweeping at once do the work once between them.
/// </para>
/// <para>
/// Each pass is its own scope and a failed pass is logged and asked again at the next interval, so a database that was
/// unreachable for one pass costs a delayed deletion and never a worker that stopped. A key outliving its window by up
/// to one interval is harmless: a repeat is answered either way, and the store holds what was asked for no longer than
/// the window plus that interval.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class MailActKeyRetentionWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<MailActKeyRetentionWorker> logger,
    TimeProvider timeProvider) : BackgroundService
{
    /// <summary>How long the worker waits between passes.</summary>
    internal static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            await Task.Delay(Interval, timeProvider, stoppingToken);
            await this.RunOnceAsync(stoppingToken);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The hosted worker isolates a failed pass so the next interval asks again.")]
    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();

            var erased = await scope.ServiceProvider
                .GetRequiredService<MailActKeyRetention>()
                .EraseExpiredAsync(stoppingToken);

            if (erased > 0)
            {
                LogKeysErased(logger, erased);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown rather than a failure: a pass cut short deleted exactly what it deleted, and what it did not
            // reach is still due on the next one.
        }
        catch (Exception exception)
        {
            LogPassFailed(logger, exception);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Deleted {ErasedKeyCount} mailbox act keys whose retention had ended.")]
    private static partial void LogKeysErased(ILogger logger, int erasedKeyCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A pass over the mailbox act keys due to be deleted failed; the next interval will ask again.")]
    private static partial void LogPassFailed(ILogger logger, Exception exception);
}
