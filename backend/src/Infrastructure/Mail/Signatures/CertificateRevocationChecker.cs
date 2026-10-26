// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MailFathom.Domain.Emails.Signatures;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Ocsp;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509.Extension;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;

namespace MailFathom.Infrastructure.Mail.Signatures;

/// <summary>Establishes whether an S/MIME signer's certificate is revoked, online, the way a mail client does.</summary>
/// <remarks>
/// <para>
/// The OCSP responder the certificate names is asked first, and the CRL its distribution point names only where no
/// responder gave a definitive answer. Both are fetched through the named client registered for this purpose, so every
/// request is bounded and retried by the same registration as any other outbound call, and the whole check is held to
/// a hard deadline on top of that.
/// </para>
/// <para>
/// <b>Only a certificate that already chains to a trusted authority reaches this.</b> That is what makes following the
/// URLs it names safe: they were written by an authority this deployment trusts rather than by whoever sent the message,
/// so a signature cannot point this process at an address of the sender's choosing. A responder learns that this
/// deployment checked the certificate, and when — which the operator page states.
/// </para>
/// <para>
/// Every failure — no URL, a refused connection, a timeout, an answer that does not verify, an answer about another
/// certificate, a list that does not speak for every end-entity certificate, a stale one — ends in <see cref="CertificateRevocationStatus.Unavailable" />, never in good and never in
/// revoked.
/// </para>
/// </remarks>
internal sealed class CertificateRevocationChecker
{
    /// <summary>The name of the outbound HTTP client OCSP requests and CRL downloads go through.</summary>
    public const string HttpClientName = "certificate-revocation";

    /// <summary>The largest OCSP response or CRL read; a public authority's list stays well inside it.</summary>
    public const long MaximumAnswerBytes = 10L * 1024 * 1024;

    /// <summary>How many OCSP responders, and separately how many CRL locations, one certificate may send requests to.</summary>
    private const int MaximumLocationsPerKind = 2;

    /// <summary>How far an answer's <c>thisUpdate</c> may lie in the future before it is not believed, for clocks that drift.</summary>
    private static readonly TimeSpan PermittedClockSkew = TimeSpan.FromMinutes(5);

    /// <summary>How old an answer that names no <c>nextUpdate</c> may be and still be believed.</summary>
    private static readonly TimeSpan LongestUndatedAnswerAge = TimeSpan.FromDays(1);

    private readonly IHttpClientFactory httpClientFactory;
    private readonly CertificateRevocationAnswerCache cache;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a checker over the registered client, the answer cache, and the clock.</summary>
    /// <param name="httpClientFactory">Opens the client registered as <see cref="HttpClientName" />, one per check.</param>
    /// <param name="cache">Holds answers until they stop being valid.</param>
    /// <param name="timeProvider">Supplies the instant an answer is judged fresh against, and the deadline's clock.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public CertificateRevocationChecker(
        IHttpClientFactory httpClientFactory,
        CertificateRevocationAnswerCache cache,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.httpClientFactory = httpClientFactory;
        this.cache = cache;
        this.timeProvider = timeProvider;
    }

    /// <summary>Establishes one certificate's revocation status.</summary>
    /// <param name="certificate">The signer's certificate.</param>
    /// <param name="issuer">The certificate that issued it, from the chain that was already found trusted.</param>
    /// <param name="timeout">How long the whole check may take, over every location the certificate names.</param>
    /// <param name="cancellationToken">Cancels the check; a cancellation the caller asked for propagates.</param>
    /// <returns>The answer, which is <see cref="CertificateRevocationAnswer.Unavailable" /> whenever none could be established.</returns>
    public async Task<CertificateRevocationAnswer> CheckAsync(
        X509Certificate2 certificate,
        X509Certificate2 issuer,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentNullException.ThrowIfNull(issuer);

        var certificateKey = CertificateKeyOf(certificate, issuer);
        if (this.cache.TryRead(certificateKey, out var cached))
        {
            return cached;
        }

        var bouncyCertificate = new Org.BouncyCastle.X509.X509CertificateParser().ReadCertificate(certificate.RawData);
        var bouncyIssuer = new Org.BouncyCastle.X509.X509CertificateParser().ReadCertificate(issuer.RawData);

        using var deadline = new CancellationTokenSource(timeout, this.timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        var (answer, validUntil) = await this.AskOcspRespondersAsync(bouncyCertificate, bouncyIssuer, linked.Token, cancellationToken);
        if (answer.Status == CertificateRevocationStatus.Unavailable)
        {
            (answer, validUntil) = await this.ReadRevocationListsAsync(bouncyCertificate, bouncyIssuer, linked.Token, cancellationToken);
        }

        this.cache.Store(certificateKey, answer, validUntil);

        return answer;
    }

    /// <summary>Tells whether a certificate names any OCSP responder or CRL location this checker would ask.</summary>
    /// <param name="certificate">The certificate to read.</param>
    /// <returns><see langword="true" /> when at least one HTTP or HTTPS location is named.</returns>
    public static bool NamesARevocationSource(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        var parsed = new Org.BouncyCastle.X509.X509CertificateParser().ReadCertificate(certificate.RawData);

        return LocationsOf(OcspLocationsOf(parsed)).Any() || LocationsOf(RevocationListLocationsOf(parsed)).Any();
    }

    /// <summary>Names a certificate by its issuer's key and its serial number, which is what a revocation answer is about.</summary>
    private static string CertificateKeyOf(X509Certificate2 certificate, X509Certificate2 issuer) =>
        $"{Convert.ToHexString(SHA256.HashData(issuer.PublicKey.ExportSubjectPublicKeyInfo()))}:{certificate.SerialNumber}";

    private static IEnumerable<Uri> LocationsOf(IEnumerable<string> candidates) =>
        candidates
            .Select(candidate => Uri.TryCreate(candidate, UriKind.Absolute, out var location) ? location : null)
            .OfType<Uri>()
            .Where(location => location.Scheme == Uri.UriSchemeHttp || location.Scheme == Uri.UriSchemeHttps)
            .Take(MaximumLocationsPerKind);

    private static IEnumerable<string> OcspLocationsOf(Org.BouncyCastle.X509.X509Certificate certificate)
    {
        var extension = certificate.GetExtensionValue(X509Extensions.AuthorityInfoAccess);
        if (extension is null)
        {
            return [];
        }

        return AuthorityInformationAccess.GetInstance(extension.GetOctets())
            .GetAccessDescriptions()
            .Where(description => description.AccessMethod.Equals(AccessDescription.IdADOcsp))
            .Select(description => description.AccessLocation)
            .Where(name => name.TagNo == GeneralName.UniformResourceIdentifier)
            .Select(name => name.Name.ToString() ?? string.Empty);
    }

    private static IEnumerable<string> RevocationListLocationsOf(Org.BouncyCastle.X509.X509Certificate certificate)
    {
        var extension = certificate.GetExtensionValue(X509Extensions.CrlDistributionPoints);
        if (extension is null)
        {
            return [];
        }

        return CrlDistPoint.GetInstance(extension.GetOctets())
            .GetDistributionPoints()
            .Select(point => point.DistributionPointName)
            .Where(name => name is not null && name.Type == DistributionPointName.FullName)
            .SelectMany(name => GeneralNames.GetInstance(name.Name).GetNames())
            .Where(name => name.TagNo == GeneralName.UniformResourceIdentifier)
            .Select(name => name.Name.ToString() ?? string.Empty);
    }

    private async Task<(CertificateRevocationAnswer Answer, DateTimeOffset? ValidUntil)> AskOcspRespondersAsync(
        Org.BouncyCastle.X509.X509Certificate certificate,
        Org.BouncyCastle.X509.X509Certificate issuer,
        CancellationToken deadline,
        CancellationToken cancellationToken)
    {
        var requestedCertificate = new CertificateID(CertificateID.DigestSha1, issuer, certificate.SerialNumber);
        var request = new OcspReqGenerator();
        request.AddRequest(requestedCertificate);
        var encodedRequest = request.Generate().GetEncoded();

        foreach (var responder in LocationsOf(OcspLocationsOf(certificate)))
        {
            var response = await this.SendAsync(
                () => new HttpRequestMessage(HttpMethod.Post, responder)
                {
                    Content = new ByteArrayContent(encodedRequest)
                    {
                        Headers = { ContentType = new MediaTypeHeaderValue("application/ocsp-request") },
                    },
                },
                deadline,
                cancellationToken);

            if (response is not null && this.ReadOcspResponse(response, requestedCertificate, issuer) is { } answered)
            {
                return answered;
            }
        }

        return (CertificateRevocationAnswer.Unavailable, null);
    }

    private (CertificateRevocationAnswer Answer, DateTimeOffset? ValidUntil)? ReadOcspResponse(
        byte[] encodedResponse,
        CertificateID requestedCertificate,
        Org.BouncyCastle.X509.X509Certificate issuer)
    {
        try
        {
            var response = new OcspResp(encodedResponse);
            if (response.Status != OcspRespStatus.Successful || response.GetResponseObject() is not BasicOcspResp basic)
            {
                return null;
            }

            if (!IsSignedByIssuerOrItsResponder(basic, issuer))
            {
                return null;
            }

            var now = this.timeProvider.GetUtcNow();
            var single = basic.Responses.FirstOrDefault(candidate => candidate.GetCertID().Equals(requestedCertificate));
            if (single is null || !IsFresh(single.ThisUpdate, single.NextUpdate, now))
            {
                return null;
            }

            DateTimeOffset? validUntil = single.NextUpdate is { } nextUpdate ? new DateTimeOffset(nextUpdate, TimeSpan.Zero) : null;

            return single.GetCertStatus() switch
            {
                null => (new CertificateRevocationAnswer(CertificateRevocationStatus.Good, RevocationCheckMethod.Ocsp, now, null), validUntil),
                RevokedStatus revoked => (
                    new CertificateRevocationAnswer(
                        CertificateRevocationStatus.Revoked,
                        RevocationCheckMethod.Ocsp,
                        now,
                        new DateTimeOffset(revoked.RevocationTime, TimeSpan.Zero)),
                    validUntil),
                _ => null,
            };
        }
        catch (Exception exception) when (IsUnreadableAnswer(exception))
        {
            return null;
        }
    }

    /// <summary>Accepts an OCSP response signed by the issuer itself, or by a responder the issuer delegated to.</summary>
    /// <remarks>
    /// A delegated responder is a certificate the response carries, issued by the same issuer and naming
    /// <c>id-kp-OCSPSigning</c>; any other signer could be anybody, so its answer is not believed.
    /// </remarks>
    private static bool IsSignedByIssuerOrItsResponder(BasicOcspResp response, Org.BouncyCastle.X509.X509Certificate issuer)
    {
        if (response.Verify(issuer.GetPublicKey()))
        {
            return true;
        }

        return response.GetCerts()
            .Where(responder => IsIssuedBy(responder, issuer))
            .Where(responder => responder.GetExtendedKeyUsage()?.Contains(KeyPurposeID.id_kp_OCSPSigning) == true)
            .Any(responder => response.Verify(responder.GetPublicKey()));
    }

    private static bool IsIssuedBy(Org.BouncyCastle.X509.X509Certificate certificate, Org.BouncyCastle.X509.X509Certificate issuer)
    {
        try
        {
            certificate.Verify(issuer.GetPublicKey());

            return certificate.IssuerDN.Equivalent(issuer.SubjectDN);
        }
        catch (Exception exception) when (IsUnreadableAnswer(exception))
        {
            return false;
        }
    }

    private async Task<(CertificateRevocationAnswer Answer, DateTimeOffset? ValidUntil)> ReadRevocationListsAsync(
        Org.BouncyCastle.X509.X509Certificate certificate,
        Org.BouncyCastle.X509.X509Certificate issuer,
        CancellationToken deadline,
        CancellationToken cancellationToken)
    {
        foreach (var location in LocationsOf(RevocationListLocationsOf(certificate)))
        {
            var list = await this.SendAsync(() => new HttpRequestMessage(HttpMethod.Get, location), deadline, cancellationToken);

            if (list is not null && this.ReadRevocationList(list, location, certificate, issuer) is { } answered)
            {
                return answered;
            }
        }

        return (CertificateRevocationAnswer.Unavailable, null);
    }

    private (CertificateRevocationAnswer Answer, DateTimeOffset? ValidUntil)? ReadRevocationList(
        byte[] encodedList,
        Uri location,
        Org.BouncyCastle.X509.X509Certificate certificate,
        Org.BouncyCastle.X509.X509Certificate issuer)
    {
        try
        {
            var list = new Org.BouncyCastle.X509.X509CrlParser().ReadCrl(encodedList);
            if (list is null || !list.IssuerDN.Equivalent(issuer.SubjectDN) || !CoversEveryEndEntityCertificate(list, location))
            {
                return null;
            }

            list.Verify(issuer.GetPublicKey());

            var now = this.timeProvider.GetUtcNow();
            if (!IsFresh(list.ThisUpdate, list.NextUpdate, now))
            {
                return null;
            }

            DateTimeOffset? validUntil = list.NextUpdate is { } nextUpdate ? new DateTimeOffset(nextUpdate, TimeSpan.Zero) : null;
            var entry = list.GetRevokedCertificate(certificate.SerialNumber);

            return entry is null
                ? (new CertificateRevocationAnswer(CertificateRevocationStatus.Good, RevocationCheckMethod.Crl, now, null), validUntil)
                : (new CertificateRevocationAnswer(
                    CertificateRevocationStatus.Revoked,
                    RevocationCheckMethod.Crl,
                    now,
                    new DateTimeOffset(entry.RevocationDate, TimeSpan.Zero)), validUntil);
        }
        catch (Exception exception) when (IsUnreadableAnswer(exception))
        {
            return null;
        }
    }

    /// <summary>Answers whether a list speaks for every end-entity certificate its issuer revoked, so its silence means good.</summary>
    /// <remarks>
    /// A list is downloaded over plain HTTP, so whoever answers can hand back any list the issuer signed. A delta list,
    /// or one scoped to authorities, attribute certificates, some revocation reasons, another distribution point, or
    /// another issuer, is signed by the same key and does not list a certificate revoked in the base — reading its silence
    /// as good would turn a revoked signer into a verified one.
    /// </remarks>
    private static bool CoversEveryEndEntityCertificate(Org.BouncyCastle.X509.X509Crl list, Uri location)
    {
        if (list.GetExtensionValue(X509Extensions.DeltaCrlIndicator) is not null)
        {
            return false;
        }

        if (list.GetExtensionValue(X509Extensions.IssuingDistributionPoint) is not { } encodedScope)
        {
            return true;
        }

        var scope = IssuingDistributionPoint.GetInstance(X509ExtensionUtilities.FromExtensionValue(encodedScope));
        if (scope.OnlyContainsCACerts || scope.OnlyContainsAttributeCerts || scope.OnlySomeReasons is not null || scope.IsIndirectCrl)
        {
            return false;
        }

        return scope.DistributionPoint switch
        {
            null => true,
            { Type: DistributionPointName.FullName } point => GeneralNames.GetInstance(point.Name).GetNames()
                .Where(name => name.TagNo == GeneralName.UniformResourceIdentifier)
                .Select(name => DerIA5String.GetInstance(name.Name).GetString())
                .Any(named => Uri.TryCreate(named, UriKind.Absolute, out var namedLocation) && namedLocation == location),
            _ => false,
        };
    }

    /// <summary>Judges whether an answer still speaks for the present.</summary>
    /// <remarks>
    /// An answer that names no <c>nextUpdate</c> says nothing about how long it holds, and it travels over plain HTTP with
    /// no nonce, so an old one replayed by anybody on the path would otherwise vouch for a certificate revoked since. It
    /// is believed only while its <c>thisUpdate</c> is within <see cref="LongestUndatedAnswerAge" />.
    /// </remarks>
    private static bool IsFresh(DateTime thisUpdate, DateTime? nextUpdate, DateTimeOffset now)
    {
        var issuedAt = new DateTimeOffset(thisUpdate, TimeSpan.Zero);

        return issuedAt <= now + PermittedClockSkew
            && (nextUpdate is { } until
                ? new DateTimeOffset(until, TimeSpan.Zero) >= now
                : issuedAt >= now - LongestUndatedAnswerAge);
    }

    /// <summary>Sends one request and reads its body, or returns nothing where no usable answer arrived.</summary>
    /// <remarks>
    /// The client is opened per request from the registration, which owns its timeout, its retries, and the size a
    /// response may reach; an answer over that size fails the read and is treated like any other missing answer.
    /// </remarks>
    private async Task<byte[]?> SendAsync(
        Func<HttpRequestMessage> createRequest,
        CancellationToken deadline,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = this.httpClientFactory.CreateClient(HttpClientName);
            using var request = createRequest();
            using var response = await client.SendAsync(request, deadline);

            return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync(deadline) : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutRejectedException
            or BrokenCircuitException or RateLimiterRejectedException)
        {
            return null;
        }
    }

    /// <summary>Recognizes what an answer a stranger's responder wrote can throw while it is read.</summary>
    private static bool IsUnreadableAnswer(Exception exception) =>
        exception is IOException or ArgumentException or InvalidOperationException or FormatException
            or OcspException or GeneralSecurityException or SecurityUtilityException or CryptoException;
}
