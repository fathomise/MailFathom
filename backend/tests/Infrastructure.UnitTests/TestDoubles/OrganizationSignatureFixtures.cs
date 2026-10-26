// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text;
using MailFathom.Infrastructure.Mail.Dkim;
using MimeKit;
using MimeKit.Cryptography;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace MailFathom.Infrastructure.UnitTests.TestDoubles;

/// <summary>Signs messages the way an organization's account does, so verification is proved against real signatures.</summary>
/// <remarks>
/// The format is ADR 0042's: Ed25519-SHA256, <c>relaxed/relaxed</c>, the nine headers each listed twice, the selector the
/// key's identifier, and the resulting <c>DKIM-Signature</c> renamed to the organization's header in place. Keys are
/// drawn per call and never leave the process.
/// </remarks>
internal static class OrganizationSignatureFixtures
{
    private static readonly string[] SignedHeaders =
    [
        "From", "From", "Reply-To", "Reply-To", "To", "To", "Cc", "Cc", "Subject", "Subject",
        "Date", "Date", "Message-ID", "Message-ID", "In-Reply-To", "In-Reply-To", "References", "References",
    ];

    /// <summary>Draws a fresh organization key.</summary>
    /// <returns>The key.</returns>
    public static OrganizationKey CreateKey()
    {
        var privateKey = new Ed25519PrivateKeyParameters(new SecureRandom());

        return new OrganizationKey(Guid.CreateVersion7(), privateKey.GeneratePublicKey().GetEncoded(), privateKey);
    }

    /// <summary>Composes an unsigned message.</summary>
    /// <param name="from">The author.</param>
    /// <param name="to">The recipients in <c>To</c>.</param>
    /// <param name="cc">The recipients in <c>Cc</c>.</param>
    /// <param name="messageId">The <c>Message-ID</c>, or <see langword="null" /> for none.</param>
    /// <returns>The message; the caller disposes it.</returns>
    public static MimeMessage Compose(
        string from = "anna@org.example.test",
        IReadOnlyList<string>? to = null,
        IReadOnlyList<string>? cc = null,
        string? messageId = "signed-1@org.example.test")
    {
        var message = new MimeMessage
        {
            Subject = "Quarterly figures",
            Date = new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero),
            Body = new TextPart("plain") { Text = "Dzień dobry." },
        };
        message.From.Add(MailboxAddress.Parse(from));
        message.To.AddRange((to ?? ["bob@org.example.test"]).Select(MailboxAddress.Parse));
        message.Cc.AddRange((cc ?? []).Select(MailboxAddress.Parse));
        message.Headers.Remove(HeaderId.MessageId);

        if (messageId is not null)
        {
            message.MessageId = messageId;
        }

        return message;
    }

    /// <summary>Signs a message with an organization key and renames the signature to the organization's header.</summary>
    /// <param name="message">The message, which is signed in place.</param>
    /// <param name="key">The key.</param>
    public static void Sign(MimeMessage message, OrganizationKey key)
    {
        var signer = new DkimSigner(key.PrivateKey, "org.example.test", key.KeyId.ToString("D"), DkimSignatureAlgorithm.Ed25519Sha256)
        {
            HeaderCanonicalizationAlgorithm = DkimCanonicalizationAlgorithm.Relaxed,
            BodyCanonicalizationAlgorithm = DkimCanonicalizationAlgorithm.Relaxed,
        };
        signer.Sign(message, SignedHeaders);

        var signature = message.Headers.First(static header => header.Id == HeaderId.DkimSignature);
        var index = message.Headers.IndexOf(signature);
        var renamed = new Header(OrganizationSignatureVerifier.HeaderName, signature.Value);
        renamed.SetRawValue(signature.RawValue);
        message.Headers.RemoveAt(index);
        message.Headers.Insert(index, renamed);
    }

    /// <summary>Serializes a message and parses it back, as a transport and a mailbox would.</summary>
    /// <param name="message">The message.</param>
    /// <param name="alter">Edits the serialized text before it is parsed again, where a test alters it in transit.</param>
    /// <returns>The bytes and the parsed message; the caller disposes the message.</returns>
    public static (byte[] RawMime, MimeMessage Parsed) RoundTrip(MimeMessage message, Func<string, string>? alter = null)
    {
        using var written = new MemoryStream();
        message.WriteTo(written);

        var rawMime = Encoding.UTF8.GetBytes((alter ?? (static text => text))(Encoding.UTF8.GetString(written.ToArray())));
        using var source = new MemoryStream(rawMime);

        return (rawMime, MimeMessage.Load(source));
    }

    /// <summary>One organization key, both halves.</summary>
    /// <param name="KeyId">The identifier, which is the selector.</param>
    /// <param name="PublicKey">The 32-byte public half.</param>
    /// <param name="PrivateKey">The private half.</param>
    public sealed record OrganizationKey(Guid KeyId, byte[] PublicKey, Ed25519PrivateKeyParameters PrivateKey);
}
