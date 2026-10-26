// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Security.Cryptography.X509Certificates;
using MailFathom.Infrastructure.Secrets.References;

namespace MailFathom.Infrastructure.Certificates;

/// <summary>Turns deployment-provisioned material into the TLS identity an endpoint presents, or into a named refusal.</summary>
/// <remarks>
/// <para>
/// This is the server-identity counterpart of <see cref="TrustAnchorLoader" /> and shares its rules: X.509 knowledge
/// stays here rather than spreading into the scheme adapters, material is erased as soon as it has been parsed, and an
/// unusable configuration produces a named failure rather than an exception.
/// </para>
/// <para>
/// The material is read by <see cref="CertificateIdentityReader" />, which the S/MIME signing identity is read by too.
/// What this loader adds is the TLS judgement, and with it the one place where it inverts the trust-anchor rule: an
/// anchor carrying a private key is rejected, while a server identity without one is, because a server that cannot
/// sign the handshake cannot prove it is the domain it claims.
/// </para>
/// </remarks>
public sealed class TlsServerCertificateLoader
{
    private readonly CertificateIdentityReader identityReader;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new server certificate loader.</summary>
    /// <param name="secretReferenceResolver">The resolver that turns a configured reference into material.</param>
    /// <param name="timeProvider">The clock the certificate's validity period is read against.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public TlsServerCertificateLoader(ISecretReferenceResolver secretReferenceResolver, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(secretReferenceResolver);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.identityReader = new CertificateIdentityReader(secretReferenceResolver);
        this.timeProvider = timeProvider;
    }

    /// <summary>Loads and validates the identity an endpoint presents for one domain.</summary>
    /// <param name="configuredMaterial">The block naming where the bundle, or the chain and its private key, come from.</param>
    /// <param name="domain">The exact DNS domain the endpoint publishes, in its ASCII form.</param>
    /// <param name="cancellationToken">Cancels the material retrieval and every password retrieval it needs.</param>
    /// <returns>The loaded identity, whose ownership passes to the caller, or a named failure.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="domain" /> is <see langword="null" />, empty, or whitespace.</exception>
    /// <remarks>
    /// The material is erased before this returns, whether loading succeeded or not, so the identity outlives the bytes
    /// it was parsed from. A returned identity has already been proven usable for <paramref name="domain" />: nothing
    /// downstream re-checks its validity period, its names, or its key usage.
    /// </remarks>
    public async Task<TlsServerCertificateLoadResult> LoadAsync(
        TlsServerCertificateOptions? configuredMaterial,
        string domain,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        var read = await this.identityReader.ReadAsync(
            configuredMaterial?.Bundle,
            configuredMaterial?.CertificateChain,
            configuredMaterial?.PrivateKey,
            cancellationToken);

        // Not disposed: Accept takes every certificate the read carried, and either publishes or releases them.
        return read.Leaf is { } leaf
            ? this.Accept(leaf, read.Supplied, domain)
            : TlsServerCertificateLoadResult.Failed(read.Failure!.Value);
    }

    /// <summary>Publishes the assembled identity, or disposes it when it cannot serve the configured domain.</summary>
    /// <remarks>
    /// The chain is ordered here rather than taken as it arrived, because neither source states an order a client can
    /// rely on: a PKCS#12 bundle states none at all, and a PEM file states whichever one it was concatenated in.
    /// </remarks>
    private TlsServerCertificateLoadResult Accept(
        X509Certificate2 leaf,
        X509Certificate2[] intermediates,
        string domain)
    {
        var evaluatedAt = this.timeProvider.GetUtcNow();
        var chainOrder = TlsServerCertificateSuitability.OrderTowardsRoot(leaf, intermediates, evaluatedAt);

        if (chainOrder.Unsuitability is { } chainFailure)
        {
            // Released here rather than through an identity, because a chain that was refused never became one.
            DisposeAll(leaf, intermediates);

            return TlsServerCertificateLoadResult.Failed(chainFailure);
        }

        var certificate = new TlsServerCertificate(leaf, chainOrder.Intermediates);
        var unsuitability = TlsServerCertificateSuitability.FindUnsuitability(leaf, domain, evaluatedAt);

        if (unsuitability is not { } failure)
        {
            return TlsServerCertificateLoadResult.Loaded(certificate);
        }

        certificate.Dispose();

        return TlsServerCertificateLoadResult.Failed(failure);
    }

    private static void DisposeAll(X509Certificate2 leaf, X509Certificate2[] intermediates)
    {
        leaf.Dispose();

        foreach (var intermediate in intermediates)
        {
            intermediate.Dispose();
        }
    }
}
