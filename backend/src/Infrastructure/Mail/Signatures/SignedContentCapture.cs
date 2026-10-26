// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MimeKit;
using MimeKit.Cryptography;

namespace MailFathom.Infrastructure.Mail.Signatures;

/// <summary>Takes the signed content and the detached signature out of a <c>multipart/signed</c> exactly as MimeKit prepares them.</summary>
/// <remarks>
/// <para>
/// Which octets a detached signature covers is the part that is easy to get subtly wrong: RFC 1847 hands the verifier
/// the first child in canonical form, with CRLF line endings and the CRLF before the boundary belonging to the boundary.
/// MimeKit already does that preparation inside <see cref="MultipartSigned.VerifyAsync(CryptographyContext, CancellationToken)" />
/// and hands the result to a context. This context is handed it and keeps it instead of verifying, so the verification
/// that follows works on the octets the library defines rather than on a second preparation written here.
/// </para>
/// <para>
/// Nothing else a context does is meaningful for it, and every other member refuses.
/// </para>
/// </remarks>
internal sealed class SignedContentCapture : CryptographyContext
{
    private static readonly string[] SignatureMediaTypes =
    [
        "application/pkcs7-signature",
        "application/x-pkcs7-signature",
        "application/pgp-signature",
    ];

    private SignedContentCapture()
    {
    }

    /// <inheritdoc />
    public override string SignatureProtocol => SignatureMediaTypes[0];

    /// <inheritdoc />
    public override string EncryptionProtocol => throw Unsupported();

    /// <inheritdoc />
    public override string KeyExchangeProtocol => throw Unsupported();

    private byte[] SignedContent { get; set; } = [];

    private byte[] Signature { get; set; } = [];

    /// <summary>Reads the signed content and the decoded signature out of one signed container.</summary>
    /// <param name="signed">The container.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The canonical signed content and the signature's decoded octets.</returns>
    /// <exception cref="FormatException">Thrown when the container lacks its two children or its signature part.</exception>
    /// <exception cref="NotSupportedException">Thrown when the container names a protocol other than S/MIME or OpenPGP.</exception>
    public static async Task<(byte[] SignedContent, byte[] Signature)> CaptureAsync(
        MultipartSigned signed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signed);

        using var capture = new SignedContentCapture();

        await signed.VerifyAsync(capture, cancellationToken);

        return (capture.SignedContent, capture.Signature);
    }

    /// <inheritdoc />
    public override bool Supports(string protocol) =>
        SignatureMediaTypes.Contains(protocol?.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override DigitalSignatureCollection Verify(
        Stream content,
        Stream signatureData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(signatureData);

        this.SignedContent = ReadAll(content);
        this.Signature = ReadAll(signatureData);

        return new DigitalSignatureCollection([]);
    }

    /// <inheritdoc />
    public override Task<DigitalSignatureCollection> VerifyAsync(
        Stream content,
        Stream signatureData,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(this.Verify(content, signatureData, cancellationToken));

    /// <inheritdoc />
    public override string GetDigestAlgorithmName(DigestAlgorithm micalg) => throw Unsupported();

    /// <inheritdoc />
    public override DigestAlgorithm GetDigestAlgorithm(string micalg) => throw Unsupported();

    /// <inheritdoc />
    public override bool CanSign(MailboxAddress signer, CancellationToken cancellationToken = default) => false;

    /// <inheritdoc />
    public override bool CanEncrypt(MailboxAddress mailbox, CancellationToken cancellationToken = default) => false;

    /// <inheritdoc />
    public override MimePart Sign(
        MailboxAddress signer,
        DigestAlgorithm digestAlgo,
        Stream content,
        CancellationToken cancellationToken = default) =>
        throw Unsupported();

    /// <inheritdoc />
    public override Task<MimePart> SignAsync(
        MailboxAddress signer,
        DigestAlgorithm digestAlgo,
        Stream content,
        CancellationToken cancellationToken = default) =>
        throw Unsupported();

    /// <inheritdoc />
    public override MimePart Encrypt(
        IEnumerable<MailboxAddress> recipients,
        Stream content,
        CancellationToken cancellationToken = default) =>
        throw Unsupported();

    /// <inheritdoc />
    public override Task<MimePart> EncryptAsync(
        IEnumerable<MailboxAddress> recipients,
        Stream content,
        CancellationToken cancellationToken = default) =>
        throw Unsupported();

    /// <inheritdoc />
    public override MimeEntity Decrypt(Stream encryptedData, CancellationToken cancellationToken = default) =>
        throw Unsupported();

    /// <inheritdoc />
    public override Task<MimeEntity> DecryptAsync(Stream encryptedData, CancellationToken cancellationToken = default) =>
        throw Unsupported();

    /// <inheritdoc />
    public override void Import(Stream stream, CancellationToken cancellationToken = default) => throw Unsupported();

    /// <inheritdoc />
    public override Task ImportAsync(Stream stream, CancellationToken cancellationToken = default) => throw Unsupported();

    /// <inheritdoc />
    public override MimePart Export(IEnumerable<MailboxAddress> mailboxes, CancellationToken cancellationToken = default) =>
        throw Unsupported();

    /// <inheritdoc />
    public override Task<MimePart> ExportAsync(
        IEnumerable<MailboxAddress> mailboxes,
        CancellationToken cancellationToken = default) =>
        throw Unsupported();

    private static byte[] ReadAll(Stream stream)
    {
        using var buffer = new MemoryStream();

        stream.CopyTo(buffer);

        return buffer.ToArray();
    }

    private static NotSupportedException Unsupported() =>
        new("The signed content capture reads a signed container and does nothing else.");
}
