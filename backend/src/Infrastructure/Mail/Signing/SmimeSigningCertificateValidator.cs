// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Secrets.References;

namespace MailFathom.Infrastructure.Mail.Signing;

/// <summary>Proves that a configured S/MIME signing certificate could sign this account's mail, or names why it could not.</summary>
/// <remarks>
/// <para>
/// The checks are the ones a recipient's mail client makes of a signature, moved to the moment the configuration is
/// read: an operator who provisioned an expired certificate, an encryption-only one, or the one for a colleague's
/// address learns it from the refusal rather than from a correspondent whose client marked the mail as suspect.
/// </para>
/// <para>
/// The certificate is not chained to a trust anchor. Which authorities a recipient trusts is the recipient's, and an
/// operator is entitled to sign with a certificate from a private authority their correspondents installed. The
/// certificates supplied beside it are held to the rules a TLS chain is, because they travel with every signature for
/// the same reason a TLS chain travels with every handshake.
/// </para>
/// <para>
/// Nothing is kept: every certificate and key read here is released before the answer is returned, and the answer is
/// a failure identity that carries no material.
/// </para>
/// </remarks>
public sealed class SmimeSigningCertificateValidator
{
    private const string SubjectAlternativeNameOid = "2.5.29.17";

    private const string ExtendedKeyUsageOid = "2.5.29.37";

    private const string KeyUsageOid = "2.5.29.15";

    private const string EmailProtectionOid = "1.3.6.1.5.5.7.3.4";

    private const string AnyExtendedKeyUsageOid = "2.5.29.37.0";

    private const string EmailAddressAttributeOid = "1.2.840.113549.1.9.1";

    private static readonly Asn1Tag Rfc822NameTag = new(TagClass.ContextSpecific, 1);

    private readonly CertificateIdentityReader identityReader;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new validator.</summary>
    /// <param name="secretReferenceResolver">The resolver that turns a configured reference into material.</param>
    /// <param name="timeProvider">The clock the certificate's validity period is read against.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public SmimeSigningCertificateValidator(ISecretReferenceResolver secretReferenceResolver, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(secretReferenceResolver);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.identityReader = new CertificateIdentityReader(secretReferenceResolver);
        this.timeProvider = timeProvider;
    }

    /// <summary>Finds the first reason the configured certificate could not sign mail sent from an address.</summary>
    /// <param name="configuredCertificate">The block naming the bundle, or the chain and its private key.</param>
    /// <param name="fromAddress">The address the account's mail is written from.</param>
    /// <param name="cancellationToken">Cancels the material retrieval and every password retrieval it needs.</param>
    /// <returns>The failure, or <see langword="null" /> when the certificate could sign as <paramref name="fromAddress" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuredCertificate" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="fromAddress" /> is <see langword="null" />, empty, or white space.</exception>
    public async Task<CertificateMaterialFailure?> FindUnusabilityAsync(
        SmimeSigningCertificateOptions configuredCertificate,
        string fromAddress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuredCertificate);
        ArgumentException.ThrowIfNullOrWhiteSpace(fromAddress);

        var read = await this.identityReader.ReadAsync(
            configuredCertificate.Bundle,
            configuredCertificate.CertificateChain,
            configuredCertificate.PrivateKey,
            cancellationToken);

        if (read.Leaf is not { } leaf)
        {
            return read.Failure;
        }

        try
        {
            var evaluatedAt = this.timeProvider.GetUtcNow();

            return TlsServerCertificateSuitability.OrderTowardsRoot(leaf, read.Supplied, evaluatedAt).Unsuitability
                ?? FindUnsuitability(leaf, fromAddress.Trim(), evaluatedAt);
        }
        finally
        {
            leaf.Dispose();

            foreach (var supplied in read.Supplied)
            {
                supplied.Dispose();
            }
        }
    }

    /// <summary>Finds the first reason a certificate cannot sign as an address, in the order an operator would fix them.</summary>
    private static CertificateMaterialFailure? FindUnsuitability(
        X509Certificate2 leaf,
        string fromAddress,
        DateTimeOffset evaluatedAt)
    {
        if (evaluatedAt < leaf.NotBefore.ToUniversalTime())
        {
            return CertificateMaterialFailure.CertificateNotYetValid;
        }

        if (evaluatedAt > leaf.NotAfter.ToUniversalTime())
        {
            return CertificateMaterialFailure.CertificateExpired;
        }

        if (!PermitsSignature(leaf))
        {
            return CertificateMaterialFailure.DigitalSignatureNotPermitted;
        }

        if (!PermitsEmailProtection(leaf))
        {
            return CertificateMaterialFailure.EmailProtectionNotPermitted;
        }

        return NamedAddressesOf(leaf).Contains(fromAddress, StringComparer.OrdinalIgnoreCase)
            ? null
            : CertificateMaterialFailure.AddressNotCoveredByCertificate;
    }

    /// <summary>Reads the key usage, treating its absence as permission rather than as refusal.</summary>
    /// <remarks>
    /// RFC 8550 § 4.4.2 lets a signing certificate assert <c>digitalSignature</c>, <c>nonRepudiation</c>, or both, so
    /// either is accepted; one asserting neither was issued for key exchange and signs nothing a recipient accepts.
    /// </remarks>
    private static bool PermitsSignature(X509Certificate2 leaf)
    {
        var declaredUsage = leaf.Extensions[KeyUsageOid];

        if (declaredUsage is null)
        {
            return true;
        }

        var keyUsages = new X509KeyUsageExtension(declaredUsage, declaredUsage.Critical).KeyUsages;

        return (keyUsages & (X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation)) != 0;
    }

    /// <summary>Reads the extended key usage, treating its absence or <c>anyExtendedKeyUsage</c> as permission.</summary>
    private static bool PermitsEmailProtection(X509Certificate2 leaf)
    {
        var declaredUsages = leaf.Extensions[ExtendedKeyUsageOid];

        if (declaredUsages is null)
        {
            return true;
        }

        return new X509EnhancedKeyUsageExtension(declaredUsages, declaredUsages.Critical).EnhancedKeyUsages
            .OfType<Oid>()
            .Any(static usage => usage.Value is EmailProtectionOid or AnyExtendedKeyUsageOid);
    }

    /// <summary>Lists every mailbox address the certificate names: its <c>rfc822Name</c> alternative names, and the legacy <c>emailAddress</c> subject attribute.</summary>
    /// <remarks>
    /// RFC 8550 § 3 asks a receiving agent to recognize both, so both are what a recipient matches the author against.
    /// The platform publishes no reader for an <c>rfc822Name</c>, so the extension is decoded here; a malformed one names
    /// nothing rather than failing, and the certificate is then refused for naming no address.
    /// </remarks>
    private static string[] NamedAddressesOf(X509Certificate2 leaf)
    {
        var subjectAddresses = leaf.SubjectName.EnumerateRelativeDistinguishedNames()
            .Where(static name => !name.HasMultipleElements
                && name.GetSingleElementType().Value == EmailAddressAttributeOid)
            .Select(static name => name.GetSingleElementValue())
            .OfType<string>();

        return [.. AlternativeAddressesOf(leaf), .. subjectAddresses];
    }

    private static string[] AlternativeAddressesOf(X509Certificate2 leaf)
    {
        if (leaf.Extensions[SubjectAlternativeNameOid] is not { } declaredNames)
        {
            return [];
        }

        var addresses = new List<string>();
        try
        {
            var generalNames = new AsnReader(declaredNames.RawData, AsnEncodingRules.DER).ReadSequence();

            while (generalNames.HasData)
            {
                if (generalNames.PeekTag() == Rfc822NameTag)
                {
                    addresses.Add(generalNames.ReadCharacterString(UniversalTagNumber.IA5String, Rfc822NameTag));
                }
                else
                {
                    generalNames.ReadEncodedValue();
                }
            }
        }
        catch (AsnContentException)
        {
            return [];
        }

        return [.. addresses];
    }
}
