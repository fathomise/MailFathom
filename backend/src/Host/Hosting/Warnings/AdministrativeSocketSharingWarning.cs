// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Host.Configuration.Endpoints;

namespace MailFathom.Host.Hosting.Warnings;

/// <summary>Warns, at every startup, about each socket the administrative surface shares with another surface.</summary>
/// <remarks>
/// <para>
/// Sharing a socket is a posture an operator selects, and one this process serves correctly: each surface keeps its own
/// credentials, grants, limits, and route prefix on it. What it cannot keep is where the administrative surface is
/// reachable from, which becomes wherever that port is published — through the ingress, the firewall rule, or the DNS
/// name the mail surface needed. That is a fact about the deployment rather than about this process, so it is stated
/// rather than refused, and stated on every start so a port nobody remembers sharing is never silent.
/// </para>
/// <para>
/// Per socket rather than once, because sharing is per socket: two surfaces may share a clear-text port and keep TLS
/// ports of their own, and the one an operator needs to look at is the one named. A clear-text socket that redirects is
/// left out, because it maps no route and answers every request with the address of a TLS socket — which is where the
/// administrative surface is actually reached, and where this warns if it is shared.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class AdministrativeSocketSharingWarning : IHostedService
{
    private readonly IReadOnlyList<ComposedListener> listeners;
    private readonly ILogger<AdministrativeSocketSharingWarning> logger;

    /// <summary>Initializes a new startup warning.</summary>
    /// <param name="listeners">The sockets this process opens, as composed from every enabled surface.</param>
    /// <param name="logger">The startup logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="listeners" /> is <see langword="null" />.</exception>
    public AdministrativeSocketSharingWarning(
        IReadOnlyList<ComposedListener> listeners,
        ILogger<AdministrativeSocketSharingWarning> logger)
    {
        ArgumentNullException.ThrowIfNull(listeners);

        this.listeners = listeners;
        this.logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var sharedWithAdministration = this.listeners.Where(static listener =>
            listener.Surfaces.HasFlag(ServedSurfaces.Admin)
            && listener.Surfaces != ServedSurfaces.Admin
            && !listener.RedirectsClearText);

        foreach (var listener in sharedWithAdministration)
        {
            this.LogAdministrativeSurfaceSharesSocket(
                listener.Address.Port,
                listener.Address.Address.ToString(),
                string.Join(", ", listener.ContributingSections));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Port {SharedPort} on {SharedBindAddress} publishes {SharingSections} together, so the administrative "
            + "endpoint is reachable wherever that port is published. Each surface still admits only its own "
            + "credentials beneath its own route prefix. Give AdminEndpoint a port of its own unless publishing it "
            + "there is the deployment you meant.")]
    private partial void LogAdministrativeSurfaceSharesSocket(
        int sharedPort,
        string sharedBindAddress,
        string sharingSections);
}
