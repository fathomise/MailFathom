// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Delivery.Receipts;
using MailFathom.Domain.Emails;
using MimeKit;
using MimeKit.Utils;

namespace MailFathom.Infrastructure.Mail.Mime;

/// <summary>Reads whether a message is a delivery report or a read receipt, and whether it asks for a read receipt.</summary>
/// <remarks>
/// <para>
/// A report is recognized by its structure alone: a <c>multipart/report</c> body (RFC 6522) whose <c>report-type</c> is
/// <c>delivery-status</c> (RFC 3464) or <c>disposition-notification</c> (RFC 8098). Nothing is inferred from a subject, a
/// sender such as <c>MAILER-DAEMON</c>, or a body's wording, because those are what a non-delivery notice written in some
/// other shape looks like too, and a message misread as a report would be withheld from every agent.
/// </para>
/// <para>
/// A field a report wrote unusably is skipped rather than guessed at, recipient by recipient: a report that names one
/// address readably and another not still says something about the first.
/// </para>
/// </remarks>
internal static class MailReceiptReading
{
    private const string DeliveryStatusReportType = "delivery-status";
    private const string DispositionNotificationReportType = "disposition-notification";

    private static readonly Dictionary<string, ReceiptOutcome> DeliveryActions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["delivered"] = ReceiptOutcome.Delivered,
        ["relayed"] = ReceiptOutcome.Relayed,
        ["expanded"] = ReceiptOutcome.Expanded,
        ["failed"] = ReceiptOutcome.Failed,
        ["delayed"] = ReceiptOutcome.Delayed,
    };

    private static readonly Dictionary<string, ReceiptOutcome> DispositionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["displayed"] = ReceiptOutcome.Displayed,
        ["deleted"] = ReceiptOutcome.Deleted,
        ["dispatched"] = ReceiptOutcome.Dispatched,
        ["processed"] = ReceiptOutcome.Processed,
    };

    /// <summary>Reads the report a message is, or nothing where it is not one.</summary>
    /// <param name="message">The parsed message.</param>
    /// <returns>What the report said, or <see langword="null" /> for every message that is not a report.</returns>
    public static ReceiptReport? ReadReport(MimeMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Body is not MultipartReport report)
        {
            return null;
        }

        if (string.Equals(report.ReportType, DeliveryStatusReportType, StringComparison.OrdinalIgnoreCase))
        {
            return ReadDeliveryReport(report);
        }

        return string.Equals(report.ReportType, DispositionNotificationReportType, StringComparison.OrdinalIgnoreCase)
            ? ReadDispositionReport(report)
            : null;
    }

    /// <summary>Reads a message's request for a read receipt, or nothing where it asks for none.</summary>
    /// <param name="message">The parsed message.</param>
    /// <returns>The request, or <see langword="null" /> where the message carries no usable <c>Disposition-Notification-To</c>.</returns>
    /// <remarks>
    /// The first mailbox the header names is the address taken. RFC 8098 lets it name several, and a mail client
    /// answering one request answers it once; a receipt sent to every address a stranger listed is a fan-out the reader
    /// never chose.
    /// </remarks>
    public static ReadReceiptRequest? ReadRequest(MimeMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Headers[HeaderId.DispositionNotificationTo] is not { } requested
            || !InternetAddressList.TryParse(requested, out var addresses)
            || addresses.Mailboxes.FirstOrDefault() is not { } mailbox
            || !EmailAddress.TryCreate(null, mailbox.Address, out var address))
        {
            return null;
        }

        string?[] senders =
        [
            ReadReturnPath(message.Headers),
            .. message.From.Mailboxes.Select(static sender => sender.Address),
        ];

        return new ReadReceiptRequest(
            address.Address,
            NamesAnotherAddress: !senders.Any(sender => string.Equals(sender, address.Address, StringComparison.OrdinalIgnoreCase)));
    }

    private static ReceiptReport ReadDeliveryReport(MultipartReport report)
    {
        var groups = report.OfType<MessageDeliveryStatus>().FirstOrDefault()?.StatusGroups;

        if (groups is not { Count: > 0 })
        {
            return new ReceiptReport(ReceiptKind.Delivery, []);
        }

        var perMessage = groups[0];
        var reportingServer = ReadTypedValue(perMessage["Reporting-MTA"]);

        ReceiptReportRecipient[] recipients =
        [
            .. groups
                .Skip(1)
                .Select(group => ReadDeliveryRecipient(group, reportingServer))
                .OfType<ReceiptReportRecipient>()
                .Take(ReceiptReport.MaximumRecipients),
        ];

        return new ReceiptReport(ReceiptKind.Delivery, recipients)
        {
            OriginalEnvelopeId = Trimmed(perMessage["Original-Envelope-Id"]),
        };
    }

    private static ReceiptReportRecipient? ReadDeliveryRecipient(HeaderList group, string? reportingServer) =>
        ReadRecipient(group) is { } recipient
        && Trimmed(group["Action"]) is { } action
        && DeliveryActions.TryGetValue(action, out var outcome)
            ? new ReceiptReportRecipient(
                recipient,
                ReadAddress(group["Original-Recipient"]),
                outcome,
                ReadStatus(group["Status"]),
                ReadTypedValue(group["Remote-MTA"]) ?? reportingServer)
            : null;

    private static ReceiptReport ReadDispositionReport(MultipartReport report)
    {
        var fields = report.OfType<MessageDispositionNotification>().FirstOrDefault()?.Fields;

        if (fields is null)
        {
            return new ReceiptReport(ReceiptKind.Read, []);
        }

        ReceiptReportRecipient[] recipients = ReadRecipient(fields) is { } recipient
            && ReadDispositionType(fields["Disposition"]) is { } outcome
                ? [new ReceiptReportRecipient(
                    recipient,
                    ReadAddress(fields["Original-Recipient"]),
                    outcome,
                    Status: null,
                    ReportingServer: null)]
                : [];

        return new ReceiptReport(ReceiptKind.Read, recipients)
        {
            OriginalMessageId = fields["Original-Message-ID"] is { } written
                ? MimeUtils.ParseMessageId(written)?.Trim() is { Length: > 0 and <= InternetMessageIdReader.MaximumLength } identifier
                    ? identifier
                    : null
                : null,
        };
    }

    private static string? ReadRecipient(HeaderList fields) => ReadAddress(fields["Final-Recipient"]);

    /// <summary>Reads the address a recipient field names.</summary>
    /// <remarks>
    /// The field is written <c>address-type; address</c>. The type is read past rather than required to be
    /// <c>rfc822</c>, because <c>utf-8</c> (RFC 6533) names the same mailbox for an internationalized address.
    /// </remarks>
    private static string? ReadAddress(string? field) =>
        ReadTypedValue(field) is { } written && EmailAddress.TryCreate(null, written, out var address)
            ? address.Address
            : null;

    /// <summary>Reads the disposition type out of a <c>Disposition</c> field, <c>action-mode/sending-mode; type/modifier</c>.</summary>
    private static ReceiptOutcome? ReadDispositionType(string? disposition)
    {
        var type = ReadTypedValue(disposition)?.Split('/', 2)[0].Trim();

        return type is not null && DispositionTypes.TryGetValue(type, out var outcome) ? outcome : null;
    }

    /// <summary>Reads an RFC 3463 status code, the first token of a <c>Status</c> field, such as <c>5.1.1</c>.</summary>
    private static string? ReadStatus(string? status) =>
        Trimmed(status)?.Split([' ', '\t', '('], 2)[0] is { Length: > 0 and <= 16 } code ? code : null;

    /// <summary>Reads the value after the type a field such as <c>Final-Recipient</c> or <c>Reporting-MTA</c> is written with.</summary>
    private static string? ReadTypedValue(string? field) =>
        Trimmed(field) is { } written && written.IndexOf(';', StringComparison.Ordinal) is var separator and >= 0
            ? Trimmed(written[(separator + 1)..])
            : null;

    private static string? ReadReturnPath(HeaderList headers) =>
        headers[HeaderId.ReturnPath] is { } written && MailboxAddress.TryParse(written, out var returnPath)
            ? returnPath.Address
            : null;

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
