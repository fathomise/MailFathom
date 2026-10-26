// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography.X509Certificates;
using MailFathom.Domain.Emails.Signatures;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Secrets.References;
using MimeKit;
using MimeKit.Cryptography;
using Org.BouncyCastle.Bcpg.OpenPgp;

namespace MailFathom.Infrastructure.Mail.Signatures;

/// <summary>Recognizes the signature a message carries and verifies it against what this deployment trusts.</summary>
/// <remarks>
/// <para>
/// Only the message's own signature is read: a body that is a <c>multipart/signed</c> or an
/// <c>application/pkcs7-mime</c> part. A signed message forwarded as an attachment, or a signed part a mailing list
/// wrapped in a footer, is not this message's signature, and is reported as not signed rather than as signed by
/// whoever signed the part inside.
/// </para>
/// <para>
/// The configured authorities and keys are loaded per verification through the same secret resolution every configured
/// certificate goes through, so a replaced root or key is used from the next signed message on. A configured entry that
/// fails to load is passed over rather than failing the message: startup has already refused a configuration whose
/// entries do not load, so one failing now is material that changed underneath a running process, and the signatures
/// the remaining entries vouch for stay verifiable.
/// </para>
/// </remarks>
internal sealed class EmailSignatureVerifier : IEmailSignatureVerifier
{
    private readonly EmailSignatureTrustOptions options;
    private readonly TrustAnchorLoader trustAnchorLoader;
    private readonly ISecretReferenceResolver secretReferenceResolver;
    private readonly SecureMimeSignatureVerifier secureMimeVerifier;

    /// <summary>Initializes a verifier over the configured trust.</summary>
    /// <param name="options">What signers are trusted against.</param>
    /// <param name="trustAnchorLoader">Loads a configured certificate authority.</param>
    /// <param name="secretReferenceResolver">Resolves a configured OpenPGP key.</param>
    /// <param name="secureMimeVerifier">Verifies an S/MIME signature once the trust is loaded.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public EmailSignatureVerifier(
        EmailSignatureTrustOptions options,
        TrustAnchorLoader trustAnchorLoader,
        ISecretReferenceResolver secretReferenceResolver,
        SecureMimeSignatureVerifier secureMimeVerifier)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(trustAnchorLoader);
        ArgumentNullException.ThrowIfNull(secretReferenceResolver);
        ArgumentNullException.ThrowIfNull(secureMimeVerifier);

        this.options = options;
        this.trustAnchorLoader = trustAnchorLoader;
        this.secretReferenceResolver = secretReferenceResolver;
        this.secureMimeVerifier = secureMimeVerifier;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A failure nothing below anticipated is reported as a signature that could not be read rather than raised. The
    /// signature is written by whoever sent the message, and an exception here would leave the message unstored and its
    /// folder's checkpoint unmoved, so the same message would stop that folder on every later run.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A signature a stranger wrote must never stop a message from being stored; whatever it makes a parser throw is reported as unreadable.")]
    public async Task<EmailSignature> VerifyAsync(
        MimeMessage message,
        string? displayedSenderAddress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            return message.Body switch
            {
                MultipartSigned signed => await this.VerifyDetachedAsync(signed, displayedSenderAddress, cancellationToken),
                ApplicationPkcs7Mime opaque => await this.VerifyOpaqueAsync(opaque, displayedSenderAddress, cancellationToken),
                _ => EmailSignature.NotSigned,
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new EmailSignature
            {
                Format = message.Body switch
                {
                    MultipartSigned signed => DetachedFormatOf(signed),
                    ApplicationPkcs7Mime => EmailSignatureFormat.SecureMimeOpaque,
                    _ => null,
                },
                UnverifiableReason = EmailSignatureUnverifiableReason.MalformedSignature,
            };
        }
    }

    private static EmailSignatureFormat? DetachedFormatOf(MultipartSigned signed) =>
        signed.ContentType.Parameters["protocol"]?.Trim() switch
        {
            { } protocol when IsNamed(protocol, "application/pkcs7-signature") || IsNamed(protocol, "application/x-pkcs7-signature") =>
                EmailSignatureFormat.SecureMimeDetached,
            { } protocol when IsNamed(protocol, "application/pgp-signature") => EmailSignatureFormat.OpenPgpMime,
            _ => null,
        };

    private static SecureMimeContentType SecureMimeTypeOf(ApplicationPkcs7Mime opaque) =>
        opaque.ContentType.Parameters["smime-type"]?.Trim() switch
        {
            { } type when IsNamed(type, "signed-data") => SecureMimeContentType.SignedData,
            { } type when IsNamed(type, "enveloped-data") => SecureMimeContentType.EnvelopedData,
            { } type when IsNamed(type, "authenveloped-data") => SecureMimeContentType.AuthEnvelopedData,
            { } type when IsNamed(type, "compressed-data") => SecureMimeContentType.CompressedData,
            { } type when IsNamed(type, "certs-only") => SecureMimeContentType.CertsOnly,
            _ => SecureMimeContentType.NotDeclared,
        };

    private static bool IsNamed(string value, string name) => string.Equals(value, name, StringComparison.OrdinalIgnoreCase);

    private static async Task<byte[]> DecodedContentOf(MimePart part, CancellationToken cancellationToken)
    {
        using var decoded = new MemoryStream();

        if (part.Content is { } content)
        {
            await content.DecodeToAsync(decoded, cancellationToken);
        }

        return decoded.ToArray();
    }

    private async Task<EmailSignature> VerifyDetachedAsync(
        MultipartSigned signed,
        string? displayedSenderAddress,
        CancellationToken cancellationToken)
    {
        if (DetachedFormatOf(signed) is not { } format)
        {
            return EmailSignature.NotSigned;
        }

        byte[] signedContent;
        byte[] signatureBlock;
        try
        {
            (signedContent, signatureBlock) = await SignedContentCapture.CaptureAsync(signed, cancellationToken);
        }
        catch (Exception exception) when (exception is FormatException or NotSupportedException)
        {
            return new EmailSignature { Format = format, UnverifiableReason = EmailSignatureUnverifiableReason.MalformedSignature };
        }

        return format == EmailSignatureFormat.OpenPgpMime
            ? OpenPgpSignatureVerifier.Verify(
                signedContent,
                signatureBlock,
                await this.LoadTrustedKeysAsync(cancellationToken),
                displayedSenderAddress)
            : await this.VerifySecureMimeAsync(
                new EmailSignature { Format = format },
                signatureBlock,
                signedContent,
                displayedSenderAddress,
                cancellationToken);
    }

    private async Task<EmailSignature> VerifyOpaqueAsync(
        ApplicationPkcs7Mime opaque,
        string? displayedSenderAddress,
        CancellationToken cancellationToken)
    {
        var secureMimeType = SecureMimeTypeOf(opaque);
        var signature = new EmailSignature { Format = EmailSignatureFormat.SecureMimeOpaque, SecureMimeType = secureMimeType };

        return secureMimeType switch
        {
            SecureMimeContentType.CertsOnly => signature,
            SecureMimeContentType.EnvelopedData or SecureMimeContentType.AuthEnvelopedData or SecureMimeContentType.CompressedData =>
                signature with { UnverifiableReason = EmailSignatureUnverifiableReason.ContentNotOpened },
            _ => await this.VerifySecureMimeAsync(
                signature,
                await DecodedContentOf(opaque, cancellationToken),
                ReadOnlyMemory<byte>.Empty,
                displayedSenderAddress,
                cancellationToken),
        };
    }

    private async Task<EmailSignature> VerifySecureMimeAsync(
        EmailSignature signature,
        ReadOnlyMemory<byte> cms,
        ReadOnlyMemory<byte> detachedContent,
        string? displayedSenderAddress,
        CancellationToken cancellationToken)
    {
        var authorities = await this.LoadTrustedAuthoritiesAsync(cancellationToken);
        try
        {
            return await this.secureMimeVerifier.VerifyAsync(
                signature,
                cms,
                detachedContent,
                new SecureMimeTrust(this.options.TrustSystemStore, authorities, this.options.RevocationTimeout),
                displayedSenderAddress,
                cancellationToken);
        }
        finally
        {
            foreach (var authority in authorities)
            {
                authority.Dispose();
            }
        }
    }

    private async Task<IReadOnlyList<X509Certificate2>> LoadTrustedAuthoritiesAsync(CancellationToken cancellationToken)
    {
        var authorities = new List<X509Certificate2>(this.options.TrustedAuthorities.Count);

        foreach (var configured in this.options.TrustedAuthorities)
        {
            var loaded = await this.trustAnchorLoader.LoadAsync(configured, cancellationToken);
            if (loaded.TrustAnchor is { } authority)
            {
                authorities.Add(authority);
            }
        }

        return authorities;
    }

    private async Task<PgpPublicKeyRingBundle> LoadTrustedKeysAsync(CancellationToken cancellationToken)
    {
        var rings = new List<PgpPublicKeyRing>();

        foreach (var configured in this.options.TrustedOpenPgpKeys)
        {
            var resolved = await this.secretReferenceResolver.ResolveAsync(configured.SecretReference, cancellationToken);
            if (resolved.Secret is not { } material)
            {
                continue;
            }

            using (material)
            {
                try
                {
                    rings.AddRange(OpenPgpSignatureVerifier.ReadPublicKeys(material.RevealBytes()));
                }
                catch (Exception exception) when (exception is PgpException or IOException)
                {
                    // Startup refuses a key that does not parse, so this is material replaced underneath a running
                    // process; the keys that still load go on vouching for their signatures.
                }
            }
        }

        return new PgpPublicKeyRingBundle(rings);
    }
}
