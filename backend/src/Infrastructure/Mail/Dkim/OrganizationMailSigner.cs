// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.Access.Organizations.SigningKeys;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Signing;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.DataEncryption;
using MailFathom.Infrastructure.Security.SigningKeys;
using MimeKit;
using MimeKit.Cryptography;
using Org.BouncyCastle.Crypto.Parameters;

namespace MailFathom.Infrastructure.Mail.Dkim;

/// <summary>Signs an organization's outgoing mail with MimeKit's DKIM signer, under a header of MailFathom's own.</summary>
/// <remarks>
/// <para>
/// The signature is a DKIM signature value (RFC 6376) made with Ed25519-SHA256 and relaxed canonicalization, written as
/// <see cref="SignatureHeaderName" /> rather than as <c>DKIM-Signature</c>, so no receiving server tries to verify a key
/// nobody published. MimeKit only writes <c>DKIM-Signature</c>, so its header is renamed here, the one place this design
/// leaves DKIM's own path; the verifier rebuilds a <c>DKIM-Signature</c> from the value in memory.
/// </para>
/// <para>
/// The message is parsed only to compute the signature. What is stored is the signature header followed by the bytes it
/// was handed, untouched, so nothing already in the message is rewritten, refolded, replaced, or removed.
/// </para>
/// <para>
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0042-signing-an-organizations-mail-and-trusting-a-sender-its-signature-proves.md">ADR 0042</see>
/// decides every tag. Each signed header is listed twice, which over-signs it: a second instance inserted later breaks
/// the signature, and a header absent at signing is signed as absent. MimeKit writes neither <c>x=</c> nor <c>l=</c>.
/// </para>
/// </remarks>
/// <param name="keys">Reads the sending account's organization and its active key.</param>
/// <param name="fieldEncryptor">Opens the key's sealed private half.</param>
/// <param name="telemetry">Counts a message an organization's account sent unsigned.</param>
/// <param name="bounds">The deployment's bound on a composed message, which the signature header may not carry it past.</param>
internal sealed class OrganizationMailSigner(
    IAccountSigningKeyReader keys,
    FieldEncryptor fieldEncryptor,
    IOrganizationSigningKeyTelemetry telemetry,
    OutgoingEmailBounds bounds)
    : IOrganizationMailSigner
{
    /// <summary>The header the signature is written in.</summary>
    internal const string SignatureHeaderName = "MailFathom-Organization-Signature";

    /// <summary>The headers the signature covers, each twice.</summary>
    private static readonly string[] SignedHeaders =
    [
        "From", "From",
        "Reply-To", "Reply-To",
        "To", "To",
        "Cc", "Cc",
        "Subject", "Subject",
        "Date", "Date",
        "Message-ID", "Message-ID",
        "In-Reply-To", "In-Reply-To",
        "References", "References",
    ];

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<byte>> SignAsync(
        MailAccountId account,
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken)
    {
        var reading = await keys.ReadAsync(account, cancellationToken);

        if (reading is AccountSigningKeyReading.Unaffiliated)
        {
            return rawMime;
        }

        if (reading is AccountSigningKeyReading.SigningKey key
            && await this.SignWithAsync(key, rawMime, cancellationToken) is { } signed)
        {
            return signed;
        }

        telemetry.MessageSentUnsigned();

        return rawMime;
    }

    /// <summary>Signs a message with an Ed25519 private key, answering with the signature header followed by the message.</summary>
    /// <param name="rawMime">The message.</param>
    /// <param name="keyId">The key's identifier, written as the selector.</param>
    /// <param name="privateHalf">The 32-byte Ed25519 private key.</param>
    /// <returns>The signed message, or <see langword="null" /> when the message names no author whose domain <c>d=</c> could carry.</returns>
    /// <remarks>Every tag is ASCII — the domain is written in its ASCII form — so the header's bytes are read and written as ASCII.</remarks>
    internal static byte[]? Sign(ReadOnlyMemory<byte> rawMime, Guid keyId, byte[] privateHalf)
    {
        using var source = new MemoryStream(rawMime.ToArray(), writable: false);
        using var message = MimeMessage.Load(source);

        if (SigningDomainOf(message) is not { } authorDomain)
        {
            return null;
        }

        var format = FormatOptions.Default.Clone();
        format.NewLineFormat = NewLineFormat.Dos;

        // The parameters keep a copy of the key that nothing here can erase; it lives only as long as this call.
        var signer = new DkimSigner(
            new Ed25519PrivateKeyParameters(privateHalf),
            authorDomain,
            keyId.ToString("D"),
            DkimSignatureAlgorithm.Ed25519Sha256)
        {
            HeaderCanonicalizationAlgorithm = DkimCanonicalizationAlgorithm.Relaxed,
            BodyCanonicalizationAlgorithm = DkimCanonicalizationAlgorithm.Relaxed,
        };

        signer.Sign(format, message, SignedHeaders);

        // MimeKit inserts its DKIM-Signature first. Its raw value keeps MimeKit's folding, and relaxed canonicalization
        // reads past any refolding anyway; only the line endings are made the ones the stored bytes carry.
        var foldedValue = Encoding.ASCII.GetString(message.Headers[0].RawValue).ReplaceLineEndings("\r\n");

        return [.. Encoding.ASCII.GetBytes($"{SignatureHeaderName}:{foldedValue}"), .. rawMime.Span];
    }

    /// <summary>Reads the domain <c>d=</c> names: the author's, in its ASCII form, as RFC 6376 writes a domain.</summary>
    /// <returns>The domain, or <see langword="null" /> where the message names no author or the author's domain has no ASCII form.</returns>
    private static string? SigningDomainOf(MimeMessage message)
    {
        if (message.From.Mailboxes.FirstOrDefault()?.Domain is not { Length: > 0 } domain)
        {
            return null;
        }

        try
        {
            return new IdnMapping().GetAscii(domain);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Signs a message with an organization's key, answering with nothing where the key or the message cannot be signed.</summary>
    /// <remarks>
    /// A message the composer measured within the deployment's bound is refused a signature that would carry it past
    /// the bound, rather than being stored and sent larger than the operator allowed.
    /// </remarks>
    private async Task<byte[]?> SignWithAsync(
        AccountSigningKeyReading.SigningKey key,
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken)
    {
        if (await this.OpenAsync(key, cancellationToken) is not { } privateHalf)
        {
            return null;
        }

        try
        {
            return Sign(rawMime, key.KeyId, privateHalf) is { } signed && signed.Length <= bounds.MaxMessageBytes
                ? signed
                : null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateHalf);
        }
    }

    /// <summary>Opens a key's private half, answering with nothing where the ring cannot open it.</summary>
    /// <remarks>
    /// A ring key retired from configuration, or a sealed half that does not open, is the deployment's fault rather than
    /// the sender's, and a send is never refused for want of a signature, so either leaves the message unsigned.
    /// </remarks>
    private async Task<byte[]?> OpenAsync(AccountSigningKeyReading.SigningKey key, CancellationToken cancellationToken)
    {
        try
        {
            return await fieldEncryptor.OpenAsync(
                OrganizationSigningKeyMinter.BindingFor(key.KeyId),
                key.SealedPrivateKey,
                cancellationToken);
        }
        catch (Exception failure) when (failure is InvalidOperationException or CryptographicException)
        {
            return null;
        }
    }
}
