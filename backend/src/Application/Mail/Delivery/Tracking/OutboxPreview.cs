// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Mail.Delivery.Tracking;

/// <summary>One send read as the message it is: everything a row of the outbox names, and the message itself.</summary>
/// <remarks>
/// <para>
/// The message is rendered from the stored MIME the send will transmit, through the same rendering a stored message is
/// read through and scanned by the same rules, so what the person reads is exactly what will leave.
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0040-each-account-outbox-as-a-read-only-list-beside-its-folders.md">ADR 0040</see>
/// decides what it carries; serving an attachment's bytes from here is not decided, so each is described and none is
/// reachable.
/// </para>
/// <para>
/// A send whose stored message could not be read is still previewed, with <see cref="Body" /> absent: the record, its
/// recipients, and what each server said are true whatever became of the bytes, and they are what a decision about the
/// send is taken from.
/// </para>
/// </remarks>
public sealed record OutboxPreview
{
    /// <summary>Gets the send, with every recipient and what each server said about it.</summary>
    public required OutgoingEmailRecord Send { get; init; }

    /// <summary>Gets whether an attempt holds the send right now.</summary>
    public required bool IsHeldByAttempt { get; init; }

    /// <summary>Gets the subject, or <see langword="null" /> where neither the record nor its message names one that could be read.</summary>
    public required string? Subject { get; init; }

    /// <summary>Gets the address the message is sent as, as its <c>From</c> header writes it, or <see langword="null" /> where the message could not be read.</summary>
    public required EmailParticipant? Sender { get; init; }

    /// <summary>Gets the message itself, or <see langword="null" /> where the stored message could not be read.</summary>
    public required EmailContentBody? Body { get; init; }

    /// <summary>Gets each attachment the message carries, by name, media type, and size.</summary>
    public required IReadOnlyList<ExtractedEmailAttachment> Attachments { get; init; }

    /// <summary>Gets the failure the send stopped on stated in words in the reader's language, or <see langword="null" /> where none is recorded.</summary>
    public required string? FailureStatement { get; init; }
}
