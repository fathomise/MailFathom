// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.AutomaticReplies;
using MailFathom.Domain.Emails;
using MimeKit;
using MimeKit.Utils;

namespace MailFathom.Infrastructure.Mail.Mime;

/// <summary>Reads an arrived message's header block for what an automatic reply needs, and nothing past it.</summary>
/// <remarks>
/// The header block alone is parsed, so a body of any shape costs nothing here: the reply needs to know who to answer,
/// whether a program or a list sent the message, and which conversation it belongs to, and every one of those is a
/// header.
/// </remarks>
internal sealed class MimeKitArrivedMailEnvelopeReader : IArrivedMailEnvelopeReader
{
    /// <inheritdoc />
    public ArrivedMailEnvelope? Read(ReadOnlyMemory<byte> rawMime)
    {
        HeaderList headers;

        try
        {
            using var stream = new MemoryStream(rawMime.ToArray(), writable: false);

            headers = HeaderList.Load(stream);
        }
        catch (FormatException)
        {
            return null;
        }

        return new ArrivedMailEnvelope(
            FirstAddressOf(headers[HeaderId.ReplyTo]) ?? FirstAddressOf(headers[HeaderId.From]),
            MailAutomationReading.Read(headers),
            IsNullSender(headers[HeaderId.ReturnPath]),
            headers[HeaderId.Subject],
            EmailThreadReferences.Create(
                MessageIdOf(headers[HeaderId.MessageId]),
                MessageIdOf(headers[HeaderId.InReplyTo]),
                headers[HeaderId.References] is { } references ? MimeUtils.EnumerateReferences(references) : null));
    }

    /// <summary>Answers whether <c>Return-Path</c> names the null sender, <c>&lt;&gt;</c>, which RFC 3834 forbids answering.</summary>
    private static bool IsNullSender(string? returnPath) =>
        returnPath is not null && string.Equals(returnPath.Trim(), "<>", StringComparison.Ordinal);

    private static string? MessageIdOf(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : MimeUtils.ParseMessageId(value);

    private static EmailAddress? FirstAddressOf(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !InternetAddressList.TryParse(value, out var addresses))
        {
            return null;
        }

        return addresses.Mailboxes
            .Select(static mailbox => EmailAddress.TryCreate(mailbox.Name, mailbox.Address, out var address) ? address : (EmailAddress?)null)
            .FirstOrDefault(static address => address is not null);
    }
}
