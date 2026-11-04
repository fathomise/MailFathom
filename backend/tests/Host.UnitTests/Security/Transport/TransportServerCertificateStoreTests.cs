// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Transport;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Secrets.References;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Transport;

/// <summary>Covers what one surface's store loads for the profiles it was given.</summary>
public sealed class TransportServerCertificateStoreTests
{
    /// <summary>
    /// A profile sharing its name with another surface's presents the certificate that profile carries, so its own store
    /// resolves nothing for it and answers no handshake for the name: the handshake finds the identity in the one store
    /// that proved it.
    /// </summary>
    [Fact]
    public async Task LoadAsync_AProfileLeavingItsCertificateToAnotherSurface_LoadsNothingAndAnswersNoHandshake()
    {
        // Arrange
        var resolver = Substitute.For<ISecretReferenceResolver>();
        var httpsSettings = new TransportHttpsOptions { ShareDomains = true };
        var profile = new TransportHttpsEndpointOptions { Name = "shared", Domain = "mail.example.test", Port = 8443 };
        httpsSettings.Endpoints.Add(profile);

        using var store = new TransportServerCertificateStore(
            httpsSettings,
            "AdminEndpoint:Https",
            new TlsServerCertificateLoader(resolver, new FakeTimeProvider()),
            new FakeTimeProvider(),
            NullLogger<TransportServerCertificateStore>.Instance);

        // Act
        await store.LoadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(store.Find(profile.ListenerAddress, "mail.example.test"));
        await resolver.DidNotReceiveWithAnyArgs().ResolveAsync(default, TestContext.Current.CancellationToken);
    }
}
