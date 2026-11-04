// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Mail.Delivery.Filing;

namespace MailFathom.Host.Hosting.Workers;

/// <summary>Runs the release of delivered sends' stored messages that the filing pass did not reach, one bounded run per interval.</summary>
/// <remarks>
/// Every replica runs the loop and the sweep's own lease decides which of them releases anything, exactly as for the
/// sweep of unlinked files. Registered on every deployment, because the payloads it releases were written whichever
/// backend holds them and whether or not the deployment still sends.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class DeliveredOutgoingContentSweepWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<DeliveredOutgoingContentSweepWorker> logger,
    TimeProvider timeProvider) : BackgroundService
{
    /// <summary>How long the worker waits before each run.</summary>
    /// <remarks>A constant rather than a setting: what is left for it is a backlog worked down once and the rare send a stopped pass left behind.</remarks>
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

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The hosted worker isolates a failed run so the next interval asks again.")]
    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();

            var released = await scope.ServiceProvider
                .GetRequiredService<DeliveredOutgoingContentSweep>()
                .RunAsync(stoppingToken);

            if (released > 0)
            {
                LogPayloadsReleased(logger, released.Value);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown rather than a failure: a run cut short has released exactly what its committed batches released.
        }
        catch (Exception exception)
        {
            LogRunFailed(logger, exception);
        }
    }

    /// <summary>Reports a run in a count alone; no identifier of a send or of its account reaches a log.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Released the stored messages of {ReleasedSendCount} delivered sends.")]
    private static partial void LogPayloadsReleased(ILogger logger, int releasedSendCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A release of delivered sends' stored messages failed; the next interval will ask again.")]
    private static partial void LogRunFailed(ILogger logger, Exception exception);
}
