// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Transport;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Transport;

/// <summary>Covers what one surface's store loads for the profiles it was given.</summary>
public sealed class TransportServerCertificateStoreTests
{
    private const string CarryingDomain = "mail.example.test";

    private const string BorrowingDomain = "shared.example.test";

    private static readonly DateTimeOffset Now = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A profile sharing its name with another surface's presents the certificate that profile carries, so its own store
    /// loads nothing for it and answers no handshake for the name — the handshake finds the identity in the one store
    /// that proved it. The profile carrying its own certificate beside it is the control: the same store, loaded in the
    /// same call, answers that name, so the absence is the skip rather than a store that loaded nothing at all.
    /// </summary>
    [Fact]
    public async Task LoadAsync_ProfilesWithAndWithoutTheirOwnCertificate_AnswersOnlyTheOneCarryingIt()
    {
        // Arrange
        using var identity = TestCertificates.CreateServerIdentity([CarryingDomain], Now.AddDays(-1), Now.AddDays(200));

        var carrying = new TransportHttpsEndpointOptions
        {
            Name = "own",
            Domain = CarryingDomain,
            Port = 8443,
            ServerCertificate = new TlsServerCertificateOptions
            {
                CertificateChain = new ConfiguredSecret
                {
                    Name = "own-chain",
                    SecretReference = $"plaintext:{TestCertificates.ToCertificateChainPem(identity)}",
                },
                PrivateKey = new ConfiguredSecret
                {
                    Name = "own-key",
                    SecretReference = $"plaintext:{TestCertificates.ToPrivateKeyPem(identity)}",
                },
            },
        };
        var borrowing = new TransportHttpsEndpointOptions { Name = "shared", Domain = BorrowingDomain, Port = 8443 };

        var httpsSettings = new TransportHttpsOptions { ShareDomains = true };
        httpsSettings.Endpoints.Add(carrying);
        httpsSettings.Endpoints.Add(borrowing);

        using var store = new TransportServerCertificateStore(
            httpsSettings,
            "AdminEndpoint:Https",
            new TlsServerCertificateLoader(new PlaintextOnlySecretReferenceResolver(), new FakeTimeProvider(Now)),
            new FakeTimeProvider(Now),
            NullLogger<TransportServerCertificateStore>.Instance);

        // Act
        await store.LoadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("own", store.Find(carrying.ListenerAddress, CarryingDomain)?.ProfileName);
        Assert.Null(store.Find(borrowing.ListenerAddress, BorrowingDomain));
    }
}
