// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Net;
using System.Text;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Mail.Signatures;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using MimeKit;
using NSubstitute;

namespace MailFathom.Infrastructure.UnitTests.TestDoubles;

/// <summary>The committed signed messages, trust material, and revocation answers, and a verifier composed over them.</summary>
/// <remarks>
/// <para>
/// Every fixture was generated once, synthetically, and is read as an embedded resource. The S/MIME signers chain to
/// <c>root-authority.pem</c> through an issuing authority each signature carries; the two responders their certificates
/// name, <see cref="OcspResponder" /> and <see cref="RevocationListLocation" />, are answered here by a fake transport,
/// so nothing reaches a network.
/// </para>
/// <para>
/// The signatures were made on 27 August 2026 and the revocation answers were issued on 1 September 2026 and stay
/// fresh for a week, which is why <see cref="Now" /> sits between them.
/// </para>
/// </remarks>
internal static class SignatureFixtures
{
    /// <summary>The OCSP responder every S/MIME signer certificate that names one names.</summary>
    public static readonly Uri OcspResponder = new("http://ocsp.signer.example.test/");

    /// <summary>The CRL distribution point every S/MIME signer certificate that names one names.</summary>
    public static readonly Uri RevocationListLocation = new("http://crl.signer.example.test/issuing.crl");

    /// <summary>Gets the instant every verification is judged at.</summary>
    public static DateTimeOffset Now { get; } = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Gets when every fixture signature says it was made.</summary>
    public static DateTimeOffset SignedAt { get; } = new(2026, 8, 27, 9, 13, 0, TimeSpan.Zero);

    /// <summary>Reads one fixture's octets.</summary>
    public static byte[] Bytes(string name)
    {
        using var resource = typeof(SignatureFixtures).Assembly.GetManifestResourceStream($"Signatures.{name}")
            ?? throw new InvalidOperationException($"No signature fixture is named {name}.");
        using var copy = new MemoryStream();
        resource.CopyTo(copy);

        return copy.ToArray();
    }

    /// <summary>Parses one fixture message.</summary>
    public static MimeMessage Message(string name) => MimeMessage.Load(new MemoryStream(Bytes(name)));

    /// <summary>Parses a message written out in the test, which is how a variant of a fixture is arranged.</summary>
    public static MimeMessage MessageFrom(string rawMime) => MimeMessage.Load(new MemoryStream(Encoding.UTF8.GetBytes(rawMime)));

    /// <summary>Answers the two responders with the named fixtures, and anything else as not found.</summary>
    /// <param name="ocspAnswer">The OCSP response fixture, or <see langword="null" /> where the responder answers nothing.</param>
    /// <param name="revocationList">The CRL fixture, or <see langword="null" /> where the distribution point serves nothing.</param>
    public static FakeHttpMessageHandler Responders(string? ocspAnswer = null, string? revocationList = null) =>
        new((request, _) => Task.FromResult(
            request.RequestUri == OcspResponder && ocspAnswer is not null ? Answer(ocspAnswer)
            : request.RequestUri == RevocationListLocation && revocationList is not null ? Answer(revocationList)
            : new HttpResponseMessage(HttpStatusCode.NotFound)));

    /// <summary>A transport on which every responder is unreachable.</summary>
    public static FakeHttpMessageHandler UnreachableResponders() =>
        new((_, _) => throw new HttpRequestException("The responder is unreachable."));

    /// <summary>Composes the verifier over the fixture trust, the given transport, and the fixed clock.</summary>
    /// <param name="responders">What the revocation responders answer.</param>
    /// <param name="trustTestAuthority">Whether the test root authority is configured as trusted.</param>
    /// <param name="trustedKeys">
    /// The OpenPGP key fixtures configured as trusted, each under its own reference. A suffix after <c>#</c> names a
    /// second reference to the same fixture, which is how one key configured twice is arranged.
    /// </param>
    public static EmailSignatureVerifier Verifier(
        FakeHttpMessageHandler responders,
        bool trustTestAuthority = true,
        params string[] trustedKeys)
    {
        var timeProvider = new FakeTimeProvider(Now);
        var resolver = new ProvisionedMaterialResolver();
        resolver.Provision("root-authority.pem", Bytes("root-authority.pem"));
        foreach (var key in trustedKeys)
        {
            resolver.Provision(key, Bytes(key.Split('#')[0]));
        }

        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(CertificateRevocationChecker.HttpClientName)
            .Returns(_ => new HttpClient(responders, disposeHandler: false));

        var options = new EmailSignatureTrustOptions
        {
            TrustSystemStore = false,
            TrustedAuthorities = trustTestAuthority ? [Configured("root-authority.pem")] : [],
            TrustedOpenPgpKeys = [.. trustedKeys.Select(Configured)],
        };

        return new EmailSignatureVerifier(
            options,
            new TrustAnchorLoader(resolver),
            resolver,
            new SecureMimeSignatureVerifier(
                new CertificateRevocationChecker(httpClientFactory, new CertificateRevocationAnswerCache(timeProvider), timeProvider),
                timeProvider));
    }

    private static ConfiguredSecret Configured(string reference) => new() { Name = $"fixture-{reference}", SecretReference = reference };

    private static HttpResponseMessage Answer(string fixture) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Bytes(fixture)) };
}
