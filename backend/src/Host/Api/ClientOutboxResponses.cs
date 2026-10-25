// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Mail.Delivery.Tracking;
using MailFathom.Domain.Delivery;

namespace MailFathom.Host.Api;

/// <summary>One page of an account's outbox, as the client draws it.</summary>
/// <param name="Sends">The sends, newest first.</param>
/// <param name="NextCursor">The cursor the next page is asked with, or <see langword="null" /> at the end of the outbox.</param>
internal sealed record ClientOutboxPageResponse(IReadOnlyList<ClientOutboxEntryResponse> Sends, string? NextCursor)
{
    /// <summary>Describes one page on the wire.</summary>
    /// <param name="page">The page the use case read.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="page" /> is <see langword="null" />.</exception>
    internal static ClientOutboxPageResponse For(AccountOutboxPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new ClientOutboxPageResponse(
            [.. page.Sends.Select(ClientOutboxEntryResponse.For)],
            page.NextCursor?.Encode());
    }
}

/// <summary>One send in an account's outbox, as a row of it names the send.</summary>
/// <param name="OutgoingEmail">The identifier the send answered with, which every decision about it names.</param>
/// <param name="Account">The account whose outbox it is in.</param>
/// <param name="Stage">The stage it stands at — <c>Recorded</c>, <c>TransmissionBegun</c>, or <c>Refused</c>.</param>
/// <param name="HeldByAttempt">Whether an attempt holds it right now, which at <c>TransmissionBegun</c> tells a send being transmitted from one whose outcome nobody knows.</param>
/// <param name="Recipients">The recipients the row names: those in <c>To</c>, or in <c>Cc</c> and then <c>Bcc</c> where <c>To</c> is empty, at most three.</param>
/// <param name="OtherRecipientCount">How many more people the message is addressed to.</param>
/// <param name="Subject">The subject, or <see langword="null" /> where it could not be read.</param>
/// <param name="RecordedAt">When the send was written down.</param>
/// <param name="DueAt">When it was written to leave at, or <see langword="null" /> where it was written to leave at once.</param>
/// <param name="DueTimeZone">The IANA zone that time was named in, present exactly when <paramref name="DueAt" /> is.</param>
/// <param name="AvailableAt">The earliest a delivery pass may take it, which for a send left to try again is when it is tried again.</param>
/// <param name="AttemptCount">How many attempts it has taken.</param>
/// <param name="LastFailureCode">The code of the failure it stopped on, or <see langword="null" /> while none is recorded.</param>
/// <param name="LastReplyCode">The reply code the server last answered the message with, or <see langword="null" />.</param>
/// <param name="Failure">That failure stated in words in the user's language, or <see langword="null" /> while none is recorded.</param>
internal sealed record ClientOutboxEntryResponse(
    Guid OutgoingEmail,
    string Account,
    string Stage,
    bool HeldByAttempt,
    IReadOnlyList<ClientOutboxRecipientResponse> Recipients,
    int OtherRecipientCount,
    string? Subject,
    DateTimeOffset RecordedAt,
    DateTimeOffset? DueAt,
    string? DueTimeZone,
    DateTimeOffset AvailableAt,
    int AttemptCount,
    int? LastFailureCode,
    int? LastReplyCode,
    string? Failure)
{
    /// <summary>Describes one row on the wire.</summary>
    /// <param name="entry">The row the use case read.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entry" /> is <see langword="null" />.</exception>
    internal static ClientOutboxEntryResponse For(AccountOutboxEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new ClientOutboxEntryResponse(
            entry.OutgoingEmailId.Value,
            entry.AccountId.Value,
            entry.Stage.ToString(),
            entry.IsHeldByAttempt,
            [.. entry.NamedRecipients.Select(ClientOutboxRecipientResponse.For)],
            entry.OtherRecipientCount,
            entry.Subject,
            entry.RecordedAt,
            entry.DueAt?.Instant,
            entry.DueAt?.ZoneId,
            entry.AvailableAt,
            entry.AttemptCount,
            entry.LastFailure?.Value,
            entry.LastReplyCode,
            entry.FailureStatement);
    }
}

/// <summary>One recipient a row of the outbox names.</summary>
/// <param name="Address">The address.</param>
/// <param name="Role">The header it is named in, as the role's own name.</param>
internal sealed record ClientOutboxRecipientResponse(string Address, string Role)
{
    /// <summary>Describes one recipient on the wire.</summary>
    /// <param name="recipient">The recipient.</param>
    /// <returns>The response body.</returns>
    internal static ClientOutboxRecipientResponse For(OutgoingRecipient recipient) => new(
        recipient.Address.Address,
        recipient.Role.ToString());
}

/// <summary>One send read as the message it is, for a caller holding both <c>mailfathom.mail.read</c> and <c>mailfathom.mail.send</c>.</summary>
/// <param name="OutgoingEmail">The identifier the send answered with.</param>
/// <param name="Account">The account it is sent from.</param>
/// <param name="Stage">The stage it stands at, as the stage's own name.</param>
/// <param name="HeldByAttempt">Whether an attempt holds it right now.</param>
/// <param name="InOutbox">Whether it is still in its account's outbox: not yet left, or refused and not dismissed.</param>
/// <param name="DismissedAt">When somebody took it out of the outbox, or <see langword="null" /> while nobody has.</param>
/// <param name="Subject">The subject, or <see langword="null" /> where none could be read.</param>
/// <param name="Sender">The address it is sent as, as its <c>From</c> header writes it, or <see langword="null" /> where the message could not be read.</param>
/// <param name="Recipients">Every recipient, with the header it is named in and what the server said about it.</param>
/// <param name="RecordedAt">When the send was written down.</param>
/// <param name="DueAt">When it was written to leave at, or <see langword="null" /> where it was written to leave at once.</param>
/// <param name="DueTimeZone">The IANA zone that time was named in, present exactly when <paramref name="DueAt" /> is.</param>
/// <param name="AvailableAt">The earliest a delivery pass may take it, which for a send left to try again is when it is tried again.</param>
/// <param name="AttemptCount">How many attempts it has taken.</param>
/// <param name="LastFailureCode">The code of the failure it stopped on, or <see langword="null" /> while none is recorded.</param>
/// <param name="LastReplyCode">The reply code the server last answered the message with, or <see langword="null" />.</param>
/// <param name="Failure">That failure stated in words in the user's language, or <see langword="null" /> while none is recorded.</param>
/// <param name="Body">The message itself, or <see langword="null" /> where the stored message could not be read.</param>
/// <param name="Attachments">Each attachment by name, media type, and size; none of them is served from here.</param>
/// <remarks>
/// The body is the same closed document a stored message's body route answers with, rendered from the MIME the send
/// will transmit, so the client draws it with the renderers it already has. All of it is mail, so none of it reaches a
/// log, a span attribute, or a telemetry event.
/// </remarks>
internal sealed record ClientOutboxPreviewResponse(
    Guid OutgoingEmail,
    string Account,
    string Stage,
    bool HeldByAttempt,
    bool InOutbox,
    DateTimeOffset? DismissedAt,
    string? Subject,
    ClientMailParticipantResponse? Sender,
    IReadOnlyList<OutboxRecipientResponse> Recipients,
    DateTimeOffset RecordedAt,
    DateTimeOffset? DueAt,
    string? DueTimeZone,
    DateTimeOffset AvailableAt,
    int AttemptCount,
    int? LastFailureCode,
    int? LastReplyCode,
    string? Failure,
    ClientOutboxPreviewBodyResponse? Body,
    IReadOnlyList<ClientOutboxAttachmentResponse> Attachments)
{
    /// <summary>Describes one preview on the wire.</summary>
    /// <param name="preview">The preview the use case read.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="preview" /> is <see langword="null" />.</exception>
    internal static ClientOutboxPreviewResponse For(OutboxPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        var send = preview.Send;

        return new ClientOutboxPreviewResponse(
            send.Id.Value,
            send.AccountId.Value,
            send.Stage.ToString(),
            preview.IsHeldByAttempt,
            send.IsInOutbox,
            send.Dismissal?.DismissedAt,
            preview.Subject,
            preview.Sender is { } sender ? ClientMailParticipantResponse.For(sender) : null,
            [.. send.Recipients.Select(OutboxRecipientResponse.For)],
            send.RecordedAt,
            send.DueAt?.Instant,
            send.DueAt?.ZoneId,
            send.AvailableAt,
            send.AttemptCount,
            send.LastFailure?.Value,
            send.LastReplyCode,
            preview.FailureStatement,
            preview.Body is { } body
                ? new ClientOutboxPreviewBodyResponse(
                    body.Availability.ToString(),
                    ClientMailBodyTextResponse.For(body.PlainText),
                    body.Document)
                : null,
            [.. preview.Attachments.Select(ClientOutboxAttachmentResponse.For)]);
    }
}

/// <summary>The message a preview carries, in the forms a stored message's body route answers with.</summary>
/// <param name="Availability">Whether the body could be read, or why not, as the state's own name.</param>
/// <param name="PlainText">The plain-text rendering, already bounded.</param>
/// <param name="Document">The closed document tree the client draws, or <see langword="null" /> where the body could not be read.</param>
internal sealed record ClientOutboxPreviewBodyResponse(
    string Availability,
    ClientMailBodyTextResponse PlainText,
    MailDocument? Document);

/// <summary>One attachment a send's message carries, described and not served.</summary>
/// <param name="FileName">The file name the message gave it, or <see langword="null" /> where it gave none.</param>
/// <param name="MediaType">Its declared media type.</param>
/// <param name="SizeOctets">Its size once decoded.</param>
internal sealed record ClientOutboxAttachmentResponse(string? FileName, string MediaType, long SizeOctets)
{
    /// <summary>Describes one attachment on the wire.</summary>
    /// <param name="attachment">The attachment the render described.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="attachment" /> is <see langword="null" />.</exception>
    internal static ClientOutboxAttachmentResponse For(ExtractedEmailAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        return new ClientOutboxAttachmentResponse(
            attachment.FileName?.Value,
            attachment.MediaType,
            attachment.DecodedSizeOctets);
    }
}
