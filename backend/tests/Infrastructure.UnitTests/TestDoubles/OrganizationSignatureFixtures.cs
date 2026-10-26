// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text;
using MailFathom.Infrastructure.Mail.Dkim;
using MimeKit;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace MailFathom.Infrastructure.UnitTests.TestDoubles;

/// <summary>Signs messages the way an organization's account does, so verification is proved against real signatures.</summary>
/// <remarks>
/// The signature is made by the production signer, so the format is exactly the one ADR 0042 decides and the outbox
/// writes. Keys are drawn per call and never leave the process.
/// </remarks>
internal static class OrganizationSignatureFixtures
{
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

    /// <summary>Signs a message as the outbox does, and puts the signature header on it in place.</summary>
    /// <param name="message">The message, which is signed in place.</param>
    /// <param name="key">The key.</param>
    /// <remarks>
    /// The signature is <see cref="OrganizationMailSigner.Sign" />'s own, over the message's CRLF serialization, so what the
    /// verifier is proved against is exactly what the outbox stores and sends.
    /// </remarks>
    public static void Sign(MimeMessage message, OrganizationKey key)
    {
        var format = FormatOptions.Default.Clone();
        format.NewLineFormat = NewLineFormat.Dos;
        using var unsigned = new MemoryStream();
        message.WriteTo(format, unsigned);

        var signed = OrganizationMailSigner.Sign(unsigned.ToArray(), key.KeyId, key.PrivateKey.GetEncoded())
            ?? throw new InvalidOperationException("The message names no author to sign for.");
        using var source = new MemoryStream(signed, writable: false);
        using var parsed = MimeMessage.Load(source);

        var signature = parsed.Headers[0];
        var copied = new Header(signature.Field, signature.Value);
        copied.SetRawValue(signature.RawValue);
        message.Headers.Insert(0, copied);
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
