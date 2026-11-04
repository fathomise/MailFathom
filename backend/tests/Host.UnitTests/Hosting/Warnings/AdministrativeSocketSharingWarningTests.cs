// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Net;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Hosting.Warnings;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Warnings;

/// <summary>Covers what an operator is told about each socket the administrative surface is published on beside another surface.</summary>
public sealed class AdministrativeSocketSharingWarningTests
{
    [Fact]
    public async Task StartAsync_TheAdministrativeSurfaceSharingASocket_WarnsNamingThePortAndTheSurfaces()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningFor(
            logs,
            Listener(8443, ServedSurfaces.Mcp | ServedSurfaces.Admin | ServedSurfaces.Client, "McpEndpoint", "AdminEndpoint", "ClientEndpoint"));

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(8443, Assert.Contains("SharedPort", record.Properties));
        Assert.Equal("McpEndpoint, AdminEndpoint, ClientEndpoint", Assert.Contains("SharingSections", record.Properties));
    }

    [Fact]
    public async Task StartAsync_TheAdministrativeSurfaceOnASocketOfItsOwn_SaysNothing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningFor(logs, Listener(8081, ServedSurfaces.Admin, "AdminEndpoint"));

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    /// <summary>A redirecting clear-text socket maps no route, so the administrative surface is not reachable on it however many surfaces share it.</summary>
    [Fact]
    public async Task StartAsync_TheAdministrativeSurfaceSharingARedirectingSocket_SaysNothing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var redirecting = Listener(8080, ServedSurfaces.Mcp | ServedSurfaces.Admin, "McpEndpoint", "AdminEndpoint") with
        {
            TerminatesTls = false,
            PresentsProfiles = false,
            RedirectsClearText = true,
        };
        var warning = WarningFor(logs, redirecting);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    /// <summary>Sharing between surfaces that do not include the administrative one publishes no administration, which is what this warning is about.</summary>
    [Fact]
    public async Task StartAsync_OtherSurfacesSharingASocket_SaysNothing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningFor(logs, Listener(8443, ServedSurfaces.Mcp | ServedSurfaces.Client, "McpEndpoint", "ClientEndpoint"));

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    private static AdministrativeSocketSharingWarning WarningFor(RecordingLoggerProvider logs, params ComposedListener[] listeners)
    {
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));

        return new AdministrativeSocketSharingWarning(
            listeners,
            loggerFactory.CreateLogger<AdministrativeSocketSharingWarning>());
    }

    private static ComposedListener Listener(int port, ServedSurfaces surfaces, params string[] sections) => new(
        new TransportHttpsListenerAddress(IPAddress.Any, port),
        surfaces,
        TerminatesTls: true,
        RedirectsClearText: false,
        PresentsProfiles: true,
        Profiles: [],
        RequestsClientCertificates: false,
        RedirectTargets: new Dictionary<string, int>(),
        ContributingSections: sections);
}
