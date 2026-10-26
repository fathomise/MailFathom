// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MailFathom.Domain.Emails.Signatures;
using MailFathom.Infrastructure.Certificates;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Cms;
using Org.BouncyCastle.Cms;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Security;

namespace MailFathom.Infrastructure.Mail.Signatures;

/// <summary>Verifies an S/MIME signature the way a mail client does: the content, the chain, the purpose, and revocation.</summary>
/// <remarks>
/// <para>
/// The signature itself is checked with BouncyCastle, which reads every CMS structure a sender can produce; the chain is
/// built with the platform's <see cref="X509Chain" />, which on Linux reads the same OpenSSL trust store every other
/// TLS connection this process makes trusts. A certificate being well formed is never enough: it has to chain to the
/// system store or to an authority the operator configured, have been valid at the signing time, be meant for signing
/// email, and not be revoked.
/// </para>
/// <para>
/// Missing intermediates are not downloaded. An S/MIME signature carries its own chain, and one that does not is not
/// verifiable here — fetching what a certificate's AIA names would send this process to an address the certificate's
/// author chose before anything about the certificate was trusted.
/// </para>
/// <para>
/// One signer is read: the first. A signature by several signers is rare in mail and no client draws more than one.
/// </para>
/// </remarks>
internal sealed class SecureMimeSignatureVerifier
{
    /// <summary>The most certificates one signature may carry before it is refused as malformed.</summary>
    /// <remarks>
    /// A signer, its intermediates, and perhaps a root or a cross-certificate are a handful. Every carried certificate
    /// is decoded and offered to chain building, so a stranger's signature is bounded before any of that is paid for.
    /// </remarks>
    public const int MaximumCarriedCertificates = 16;

    /// <summary>The extended key usage a signing certificate names, <c>id-kp-emailProtection</c>.</summary>
    private const string EmailProtectionOid = "1.3.6.1.5.5.7.3.4";

    /// <summary>The chain statuses that are about validity or purpose rather than about trust, which the later steps report.</summary>
    private const X509ChainStatusFlags ValidityAndPurposeStatuses =
        X509ChainStatusFlags.NotTimeValid | X509ChainStatusFlags.NotTimeNested | X509ChainStatusFlags.NotValidForUsage;

    private readonly CertificateRevocationChecker revocationChecker;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a verifier over the revocation checker and the clock.</summary>
    /// <param name="revocationChecker">Establishes a trusted signer's revocation status.</param>
    /// <param name="timeProvider">Supplies the verification time where a signature claims no signing time.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public SecureMimeSignatureVerifier(CertificateRevocationChecker revocationChecker, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(revocationChecker);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.revocationChecker = revocationChecker;
        this.timeProvider = timeProvider;
    }

    /// <summary>Verifies one CMS signature.</summary>
    /// <param name="signature">The form and declared type, which the result carries.</param>
    /// <param name="cms">The CMS <c>SignedData</c>: a detached signature, or an opaque part carrying its content.</param>
    /// <param name="detachedContent">The canonical signed content for a detached signature; empty for an opaque one.</param>
    /// <param name="trust">The authorities trusted for this verification, already loaded.</param>
    /// <param name="displayedSenderAddress">The address the message's <c>From</c> header wrote.</param>
    /// <param name="cancellationToken">Cancels the revocation check.</param>
    /// <returns>The verification.</returns>
    public async Task<EmailSignature> VerifyAsync(
        EmailSignature signature,
        ReadOnlyMemory<byte> cms,
        ReadOnlyMemory<byte> detachedContent,
        SecureMimeTrust trust,
        string? displayedSenderAddress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(trust);

        if (!TryReadFirstSigner(cms, detachedContent, out var signedData, out var signer))
        {
            return signature with { UnverifiableReason = EmailSignatureUnverifiableReason.MalformedSignature };
        }

        var signedAt = SigningTimeOf(signer);
        signature = signature with { SignedAt = signedAt };

        var carriedCertificates = signedData.GetCertificates().EnumerateMatches(null).ToArray();
        if (carriedCertificates.Length > MaximumCarriedCertificates)
        {
            return signature with { UnverifiableReason = EmailSignatureUnverifiableReason.MalformedSignature };
        }

        var signerCertificate = signedData.GetCertificates().EnumerateMatches(signer.SignerID).FirstOrDefault();
        if (signerCertificate is null)
        {
            return signature with
            {
                ContentCheck = SignatureCheckResult.NotChecked,
                AuthorityCheck = SignatureCheckResult.Failed,
                UnverifiableReason = EmailSignatureUnverifiableReason.NoCertificate,
            };
        }

        var contentCheck = VerifyContent(signer, signerCertificate);
        if (contentCheck is null)
        {
            return signature with { UnverifiableReason = EmailSignatureUnverifiableReason.UnsupportedAlgorithm };
        }

        using var certificate = X509CertificateLoader.LoadCertificate(signerCertificate.GetEncoded());
        var carried = new List<X509Certificate2>(carriedCertificates.Length);

        try
        {
            // Loaded inside the try so a certificate that fails to load leaves none of those before it undisposed.
            foreach (var carriedCertificate in carriedCertificates)
            {
                carried.Add(X509CertificateLoader.LoadCertificate(carriedCertificate.GetEncoded()));
            }

            return await this.JudgeSignerAsync(
                signature with { ContentCheck = contentCheck.Value, Signer = DescribeSigner(certificate, signer, displayedSenderAddress) },
                certificate,
                [.. carried],
                trust,
                signedAt ?? this.timeProvider.GetUtcNow(),
                displayedSenderAddress,
                cancellationToken);
        }
        finally
        {
            foreach (var carriedCertificate in carried)
            {
                carriedCertificate.Dispose();
            }
        }
    }

    private static bool TryReadFirstSigner(
        ReadOnlyMemory<byte> cms,
        ReadOnlyMemory<byte> detachedContent,
        out CmsSignedData signedData,
        out SignerInformation signer)
    {
        signedData = null!;
        signer = null!;

        try
        {
            signedData = detachedContent.IsEmpty
                ? new CmsSignedData(cms.ToArray())
                : new CmsSignedData(new CmsProcessableByteArray(detachedContent.ToArray()), cms.ToArray());

            var first = signedData.GetSignerInfos().GetSigners().FirstOrDefault();
            if (first is null)
            {
                return false;
            }

            signer = first;

            return true;
        }
        catch (Exception exception) when (IsUnreadableSignature(exception))
        {
            return false;
        }
    }

    private static DateTimeOffset? SigningTimeOf(SignerInformation signer)
    {
        try
        {
            var attribute = signer.SignedAttributes?[CmsAttributes.SigningTime];

            return attribute is { AttrValues.Count: > 0 }
                ? new DateTimeOffset(Time.GetInstance(attribute.AttrValues[0]).ToDateTime(), TimeSpan.Zero)
                : null;
        }
        catch (Exception exception) when (IsUnreadableSignature(exception))
        {
            return null;
        }
    }

    /// <summary>Checks the signature against the content, or reports nothing where the algorithm is one that cannot be verified.</summary>
    /// <remarks>
    /// It verifies against the certificate's public key rather than the certificate, because the certificate overload
    /// also refuses a certificate not valid at the signing time and would report an expired signer as altered content.
    /// Validity is judged with the chain instead, as a step of its own.
    /// </remarks>
    private static SignatureCheckResult? VerifyContent(SignerInformation signer, Org.BouncyCastle.X509.X509Certificate certificate)
    {
        try
        {
            return signer.Verify(certificate.GetPublicKey()) ? SignatureCheckResult.Passed : SignatureCheckResult.Failed;
        }
        catch (Exception exception) when (exception is SecurityUtilityException
            || exception.InnerException is SecurityUtilityException)
        {
            return null;
        }
        catch (Exception exception) when (IsUnreadableSignature(exception))
        {
            return SignatureCheckResult.Failed;
        }
    }

    private async Task<EmailSignature> JudgeSignerAsync(
        EmailSignature signature,
        X509Certificate2 certificate,
        X509Certificate2[] carried,
        SecureMimeTrust trust,
        DateTimeOffset judgedAt,
        string? displayedSenderAddress,
        CancellationToken cancellationToken)
    {
        var chain = BuildChain(certificate, carried, trust, judgedAt);
        try
        {
            var authorityPassed = chain.Anchor is not null;
            var validityFailure = FindValidityFailure(certificate, chain.Statuses);

            signature = signature with
            {
                AuthorityCheck = authorityPassed ? SignatureCheckResult.Passed : SignatureCheckResult.Failed,
                TrustAnchor = chain.Anchor,
                ChainEnd = chain.Chain.ChainElements.Count > 0 ? chain.Chain.ChainElements[^1].Certificate.Subject : null,
                ValidityCheck = validityFailure is null ? SignatureCheckResult.Passed : SignatureCheckResult.Failed,
                ValidityFailure = validityFailure,
                SignerCheck = CertificateMailAddresses.Of(certificate)
                    .Any(address => string.Equals(address, displayedSenderAddress, StringComparison.OrdinalIgnoreCase))
                    ? SignatureCheckResult.Passed
                    : SignatureCheckResult.Failed,
            };

            var revocation = authorityPassed
                ? await this.CheckChainRevocationAsync(chain.Chain, trust.RevocationTimeout, cancellationToken)
                : null;

            signature = signature with
            {
                RevocationCheck = revocation?.Status switch
                {
                    null => SignatureCheckResult.Skipped,
                    CertificateRevocationStatus.Good => SignatureCheckResult.Passed,
                    CertificateRevocationStatus.Revoked => SignatureCheckResult.Failed,
                    _ => SignatureCheckResult.NotChecked,
                },
                RevocationMethod = revocation?.Method,
                RevocationCheckedAt = revocation?.CheckedAt,
                RevokedAt = revocation?.RevokedAt,
            };

            return signature.ContentCheck == SignatureCheckResult.Failed
                ? signature
                : signature with { UnverifiableReason = FirstUnverifiableReason(signature) };
        }
        finally
        {
            chain.Chain.Dispose();
        }
    }

    /// <summary>Asks about every certificate in the chain below its root, from the signer upwards, and stops at the first answer that is not good.</summary>
    /// <remarks>
    /// <para>
    /// An authority revoked by the root above it can still sign: a leaked intermediate key issues a certificate for any
    /// address and vouches for it in its own OCSP answers. Asking about the signer alone would miss exactly that, so each
    /// link is asked about against the certificate that issued it. The answer returned is the signer's when every link
    /// is good, and otherwise the first link's that is not, so a revoked authority reads as revoked.
    /// </para>
    /// <para>
    /// An authority above the signer that names no OCSP responder and no CRL is passed over rather than counted as
    /// uncheckable: nobody can revoke it, its issuer having published nowhere to say so, which is the same knowledge a
    /// mail client has of it. The signer itself is held to having a source, as before. Stopping at the first answer that
    /// is not good keeps the wait within one <c>RevocationTimeout</c> per certificate actually asked about.
    /// </para>
    /// </remarks>
    private async Task<CertificateRevocationAnswer?> CheckChainRevocationAsync(
        X509Chain chain,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var elements = chain.ChainElements;
        CertificateRevocationAnswer? signerAnswer = null;

        for (var index = 0; index < elements.Count - 1; index++)
        {
            var link = elements[index].Certificate;
            if (index > 0 && !CertificateRevocationChecker.NamesARevocationSource(link))
            {
                continue;
            }

            var answer = await this.revocationChecker.CheckAsync(link, elements[index + 1].Certificate, timeout, cancellationToken);
            if (answer.Status != CertificateRevocationStatus.Good)
            {
                return answer;
            }

            signerAnswer ??= answer;
        }

        return signerAnswer;
    }

    private static EmailSignatureUnverifiableReason? FirstUnverifiableReason(EmailSignature signature) =>
        signature.AuthorityCheck != SignatureCheckResult.Passed ? EmailSignatureUnverifiableReason.UntrustedRoot
        : signature.ValidityFailure is { } validityFailure ? validityFailure
        : signature.RevocationCheck switch
        {
            SignatureCheckResult.Passed => null,
            SignatureCheckResult.Failed => EmailSignatureUnverifiableReason.Revoked,
            _ => EmailSignatureUnverifiableReason.RevocationNotCheckable,
        };

    /// <summary>Builds the chain against the system store first and the configured authorities second.</summary>
    /// <remarks>
    /// Two builds rather than one, because the platform trusts either the system store or a custom set and never both at
    /// once. The configured authorities are asked only where the system store did not already establish trust, so a
    /// certificate a public authority issued is reported as the public authority's whatever else is configured. Where
    /// neither trusts the certificate, the last build attempted is the one reported, which still names every validity
    /// and purpose problem and the certificate the chain ended at.
    /// </remarks>
    private static TrustedChain BuildChain(
        X509Certificate2 certificate,
        X509Certificate2[] carried,
        SecureMimeTrust trust,
        DateTimeOffset judgedAt)
    {
        TrustedChain? attempt = null;

        if (trust.TrustSystemStore)
        {
            attempt = Build(certificate, carried, configuredAuthorities: null, judgedAt, SignatureTrustAnchor.SystemTrustStore);
            if (attempt.Anchor is not null || trust.ConfiguredAuthorities.Count == 0)
            {
                return attempt;
            }
        }

        attempt?.Chain.Dispose();

        return Build(certificate, carried, trust.ConfiguredAuthorities, judgedAt, SignatureTrustAnchor.ConfiguredAuthority);
    }

    private static TrustedChain Build(
        X509Certificate2 certificate,
        X509Certificate2[] carried,
        IReadOnlyList<X509Certificate2>? configuredAuthorities,
        DateTimeOffset judgedAt,
        SignatureTrustAnchor anchor)
    {
        X509Chain? chain = new();
        try
        {
            chain.ChainPolicy.ExtraStore.AddRange(carried);
            chain.ChainPolicy.ApplicationPolicy.Add(new Oid(EmailProtectionOid));
            chain.ChainPolicy.DisableCertificateDownloads = true;

            // Revocation is established online by the checker instead, through the bounded client this deployment
            // registered, rather than by the platform's own fetcher, which no registration reaches.
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
            chain.ChainPolicy.VerificationTime = judgedAt.UtcDateTime;

            if (configuredAuthorities is not null)
            {
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                foreach (var authority in configuredAuthorities)
                {
                    chain.ChainPolicy.CustomTrustStore.Add(authority);
                }
            }

            chain.Build(certificate);

            var statuses = chain.ChainStatus.Aggregate(X509ChainStatusFlags.NoError, (all, status) => all | status.Status);
            var trusted = chain.ChainElements.Count > 0 && (statuses & ~ValidityAndPurposeStatuses) == X509ChainStatusFlags.NoError;

            var built = new TrustedChain(chain, statuses, trusted ? anchor : null);
            chain = null;

            return built;
        }
        finally
        {
            chain?.Dispose();
        }
    }

    /// <summary>Finds what makes the signer unfit at the signing time: an expired link, or a purpose other than signing email.</summary>
    private static EmailSignatureUnverifiableReason? FindValidityFailure(X509Certificate2 certificate, X509ChainStatusFlags statuses)
    {
        if (statuses.HasFlag(X509ChainStatusFlags.NotTimeValid))
        {
            return EmailSignatureUnverifiableReason.OutsideValidityPeriod;
        }

        var keyUsage = certificate.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        var signsDigitally = keyUsage is null
            || keyUsage.KeyUsages.HasFlag(X509KeyUsageFlags.DigitalSignature)
            || keyUsage.KeyUsages.HasFlag(X509KeyUsageFlags.NonRepudiation);

        return statuses.HasFlag(X509ChainStatusFlags.NotValidForUsage) || !signsDigitally
            ? EmailSignatureUnverifiableReason.WrongKeyUsage
            : null;
    }

    private static EmailSigner DescribeSigner(
        X509Certificate2 certificate,
        SignerInformation signer,
        string? displayedSenderAddress)
    {
        var addresses = CertificateMailAddresses.Of(certificate);

        return new EmailSigner
        {
            Name = certificate.Subject,
            Address = addresses.FirstOrDefault(address => string.Equals(address, displayedSenderAddress, StringComparison.OrdinalIgnoreCase))
                ?? (addresses.Length > 0 ? addresses[0] : null),
            Issuer = certificate.Issuer,
            ValidFrom = new DateTimeOffset(certificate.NotBefore.ToUniversalTime(), TimeSpan.Zero),
            ValidUntil = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero),
            SerialNumber = certificate.SerialNumber,
            Fingerprint = Convert.ToHexString(SHA256.HashData(certificate.RawData)),
            PublicKeyAlgorithm = PublicKeyAlgorithmOf(certificate),
            DigestAlgorithm = DigestAlgorithmOf(signer.DigestAlgorithmID.Algorithm.Id),
            KeyMaterial = certificate.RawData,
        };
    }

    private static string? PublicKeyAlgorithmOf(X509Certificate2 certificate)
    {
        using var rsa = certificate.GetRSAPublicKey();
        if (rsa is not null)
        {
            return $"RSA {rsa.KeySize}";
        }

        using var ecdsa = certificate.GetECDsaPublicKey();
        if (ecdsa is not null)
        {
            return $"ECDSA {CurveNameOf(ecdsa)}";
        }

        return certificate.PublicKey.Oid.FriendlyName ?? certificate.PublicKey.Oid.Value;
    }

    private static string CurveNameOf(ECDsa key)
    {
        var curve = key.ExportParameters(includePrivateParameters: false).Curve;

        return curve.Oid.Value switch
        {
            "1.2.840.10045.3.1.7" => "P-256",
            "1.3.132.0.34" => "P-384",
            "1.3.132.0.35" => "P-521",
            _ => curve.Oid.FriendlyName ?? curve.Oid.Value ?? $"{key.KeySize}",
        };
    }

    private static string? DigestAlgorithmOf(string digestOid)
    {
        try
        {
            return DigestUtilities.GetAlgorithmName(new DerObjectIdentifier(digestOid));
        }
        catch (Exception exception) when (IsUnreadableSignature(exception))
        {
            return null;
        }
    }

    /// <summary>Recognizes what a signature a stranger wrote can throw while it is read.</summary>
    private static bool IsUnreadableSignature(Exception exception) =>
        exception is CmsException or IOException or ArgumentException or InvalidOperationException or FormatException
            or InvalidCastException or GeneralSecurityException or SecurityUtilityException or CryptoException
            or CryptographicException;

    /// <summary>One built chain, the statuses it reported, and the anchor that trusted it where one did.</summary>
    private sealed record TrustedChain(X509Chain Chain, X509ChainStatusFlags Statuses, SignatureTrustAnchor? Anchor);
}
