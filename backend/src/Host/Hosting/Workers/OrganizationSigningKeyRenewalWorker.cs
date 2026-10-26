// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access.Organizations.SigningKeys;

namespace MailFathom.Host.Hosting.Workers;

/// <summary>Runs the pass that mints missing organization signing keys and rotates the ones due, once per interval.</summary>
/// <remarks>
/// Every replica runs the loop, and each organization's renewal takes a lease of its own, so a pass never renews one
/// organization twice. Registered on every deployment: a deployment without a key ring still runs it, because counting
/// the organizations it cannot give a key is how an operator learns the ring is missing.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class OrganizationSigningKeyRenewalWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<OrganizationSigningKeyRenewalWorker> logger,
    TimeProvider timeProvider) : BackgroundService
{
    /// <summary>How long the worker waits before each pass.</summary>
    /// <remarks>
    /// A constant rather than a setting: it is how late a rotation due after a year may run, and how soon after the
    /// upgrade an organization that predates signing gets its first key — an hour is negligible against both.
    /// </remarks>
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

            var pass = await scope.ServiceProvider
                .GetRequiredService<OrganizationSigningKeyRenewal>()
                .RunAsync(stoppingToken);

            if (pass.Renewed > 0)
            {
                LogKeysRenewed(logger, pass.Renewed);
            }

            if (pass.Unminted > 0)
            {
                LogKeysUnminted(logger, pass.Unminted);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown rather than a failure: every renewal already committed stands, and the rest are due next pass.
        }
        catch (Exception exception)
        {
            LogPassFailed(logger, exception);
        }
    }

    /// <summary>Reports a pass in a count alone; no organization, key, or key material reaches a log.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Gave {RenewedOrganizationCount} organizations a fresh signing key, minting a missing one or rotating one that was due.")]
    private static partial void LogKeysRenewed(ILogger logger, int renewedOrganizationCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{UnmintedOrganizationCount} organizations are due a signing key and got none, because no "
            + "data-encryption key ring is configured; they hold no key to sign with until one is.")]
    private static partial void LogKeysUnminted(ILogger logger, int unmintedOrganizationCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A pass over the organizations' signing keys failed; the next interval will ask again.")]
    private static partial void LogPassFailed(ILogger logger, Exception exception);
}
