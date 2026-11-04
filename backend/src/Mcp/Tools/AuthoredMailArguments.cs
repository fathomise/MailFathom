// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Submission;
using MailFathom.Application.Mail.Delivery.Tracking;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Scheduling;

namespace MailFathom.Mcp.Tools;

/// <summary>Reads the identifiers, the idempotency key, and the account name that the tools authoring mail, asking about a send, and anchoring a request to one stored email share.</summary>
/// <remarks>
/// <para>
/// An email named for a read, for a flag change, or for an answer is the same identifier, and so are a queued send and
/// a held draft; the three tools that send take an idempotency key in the same shape. Reading each of them once is what
/// keeps one question from drifting into as many answers as there are tools — a key a fourth character longer here than
/// there, an identifier one tool parses and the next scans unbounded. The address lists those tools take are not read
/// here: <see cref="Application.Mail.Delivery.Addressing.AuthoredRecipientHeaders" /> holds them, so a recipient
/// shape is decided once for every surface rather than once for this one.
/// </para>
/// <para>
/// Everything here is checked in front of the use case rather than instead of it. The domain bounds the same key where
/// its column is bounded, and the use case decides whether an account this text could name is one this deployment
/// serves, so what these refusals buy is a caller meeting a statement about the argument it sent rather than an
/// argument failure naming a parameter it never wrote.
/// </para>
/// </remarks>
internal static class AuthoredMailArguments
{
    /// <summary>The greatest length text naming an email may carry before anything tries to read an identity out of it.</summary>
    /// <remarks>The bound and the reason are <c>get_email_content</c>'s: the longest form <see cref="Guid.TryParse(string, out Guid)" /> accepts is 68 characters, and the parse scans whatever it is handed.</remarks>
    private const int MaximumIdentifierLength = 68;

    /// <summary>The greatest length a send time may be written in before anything parses it.</summary>
    /// <remarks>The longest form <see cref="SendTimeFormats" /> accepts, seven fractional digits and an offset, is 33 characters; the bound sits above it and keeps an implausible value from being scanned.</remarks>
    private const int MaximumSendTimeLength = 40;

    /// <summary>The ISO 8601 shapes a send time is read in: to the minute, the second, or a fraction of one, each with an offset or a <c>Z</c>, or with neither.</summary>
    private static readonly string[] SendTimeFormats =
    [
        "yyyy-MM-dd'T'HH:mmK",
        "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
    ];

    /// <summary>Reads the identity of the stored email an answer is anchored to.</summary>
    /// <param name="storedEmailId">The text the caller named the email by.</param>
    /// <returns>The email identity.</returns>
    /// <remarks>
    /// <para>
    /// The refusal is the malformed-identifier one rather than the answer a missing email gets, and the two are
    /// deliberately different: this one says the request never named an email at all, which is true whatever this
    /// deployment holds, while an email that was named and cannot be answered is answered identically whether it is
    /// absent, withheld, or unreadable. The empty UUID is refused here with everything else, because it is what a
    /// client sends when it holds no identifier.
    /// </para>
    /// <para>
    /// The refused text is deliberately absent from the failure. It is the caller's own input on its way into a
    /// client-readable result and the log line beside it, and an identifier a caller invented says nothing an operator
    /// needs that the code does not already say. Which entry of a list was refused is absent for the same reason it is
    /// unnecessary: the caller holds the list it sent.
    /// </para>
    /// <para>
    /// Every tool that anchors a request to one stored email reads it here — the read, the flag change, and the three
    /// that author an answer — so what a well-formed identifier is cannot be one thing at one tool and another at the
    /// next.
    /// </para>
    /// </remarks>
    /// <exception cref="StoredEmailIdentifierMalformedException">Thrown when the text is not an identifier this system issues.</exception>
    public static StoredEmailId AnsweredEmail(string storedEmailId)
    {
        if (storedEmailId is null
            || storedEmailId.Length > MaximumIdentifierLength
            || !Guid.TryParse(storedEmailId, out var parsed)
            || parsed == Guid.Empty)
        {
            throw new StoredEmailIdentifierMalformedException();
        }

        return StoredEmailId.Create(parsed);
    }

    /// <summary>Reads the identity of the send a caller is asking about.</summary>
    /// <param name="outgoingEmailId">The text the caller named the queued message by.</param>
    /// <returns>The record identity.</returns>
    /// <exception cref="QueuedSendRefusedException">Thrown when the text is not an identifier this system issues.</exception>
    /// <remarks>
    /// The bound and the parse are the stored email's, for the same reason: the longest form
    /// <see cref="Guid.TryParse(string, out Guid)" /> accepts is 68 characters and the parse scans whatever it is
    /// handed. What differs is the refusal, because the two identify different things and a caller told its send
    /// identifier is not an email identifier would be looking for the wrong mistake.
    /// </remarks>
    public static OutgoingEmailId QueuedSend(string outgoingEmailId)
    {
        if (outgoingEmailId is null
            || outgoingEmailId.Length > MaximumIdentifierLength
            || !Guid.TryParse(outgoingEmailId, out var parsed)
            || parsed == Guid.Empty)
        {
            throw QueuedSendRefusedException.IdentifierMalformed();
        }

        return OutgoingEmailId.Create(parsed);
    }

    /// <summary>Reads the identity of the draft a caller is naming.</summary>
    /// <param name="draftId">The text the caller named the draft by.</param>
    /// <returns>The draft identity.</returns>
    /// <remarks>
    /// Text that is not an identifier at all is answered as a draft this deployment does not hold, which is the answer
    /// a draft of another account, a draft already given up, and a draft nobody ever wrote all get. That is deliberate
    /// where it is deliberate for those three: telling them apart is how a caller would learn which drafts exist by
    /// asking about them, and a draft carries no published identifier scheme a caller could be told it violated. The
    /// bound and the parse are the stored email's, because the longest form <see cref="Guid.TryParse(string, out Guid)" />
    /// accepts is 68 characters and the parse scans whatever it is handed.
    /// </remarks>
    /// <exception cref="MailDraftRefusedException">Thrown when the text is not an identifier this system issues.</exception>
    public static MailDraftId HeldDraft(string draftId)
    {
        if (draftId is null
            || draftId.Length > MaximumIdentifierLength
            || !Guid.TryParse(draftId, out var parsed)
            || parsed == Guid.Empty)
        {
            throw MailDraftRefusedException.NotFound();
        }

        return MailDraftId.Create(parsed);
    }

    /// <summary>Names the invocation asking, from the key the caller supplied for it.</summary>
    /// <param name="idempotencyKey">The caller's own identity for this send.</param>
    /// <returns>The requester the record is written under.</returns>
    /// <exception cref="MailSubmissionRefusedException">Thrown when the key is not one a record can be written under.</exception>
    public static OutgoingEmailRequester Requester(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey)
            || idempotencyKey.Length > OutgoingEmailRequester.MaximumIdentityLength
            || idempotencyKey.Any(char.IsControl))
        {
            throw MailSubmissionRefusedException.IdempotencyKeyUnusable();
        }

        return OutgoingEmailRequester.Command(idempotencyKey);
    }

    /// <summary>Reads the time a send is to be held until, in either of the two forms a caller may name one.</summary>
    /// <param name="sendAt">The time, or absent to send as soon as a delivery pass reaches the message.</param>
    /// <param name="sendAtTimeZone">The IANA zone a time written without an offset is read in, or absent where the time carries its own offset.</param>
    /// <returns>The instant together with the zone it was named in, or <see langword="null" /> where no time was named.</returns>
    /// <exception cref="MailSubmissionRefusedException">Thrown when the time is unreadable, states an offset beside a zone, names neither, names a zone this host does not know, or when a zone is named with no time.</exception>
    /// <remarks>
    /// <para>
    /// The two forms are the two ways into <see cref="ZonedInstant" />. A time carrying an offset is already an instant
    /// and is recorded as named in no zone of its own. A wall-clock time beside a zone is resolved in that zone exactly
    /// once, here, because this is where "which nine in the morning" can still be answered: a caller knows nine on
    /// Monday in Europe/Warsaw far more reliably than it knows the offset Warsaw will stand at on that Monday.
    /// </para>
    /// <para>
    /// Only ISO 8601 is read, through formats spelled out rather than a culture's parse, so a day and a month are never
    /// read in each other's place. Whether the time has already passed, and whether it lies further ahead than the
    /// deployment holds a message, are the use case's questions: it reads the clock, and every surface meets the same
    /// answer there.
    /// </para>
    /// </remarks>
    public static ZonedInstant? DueTime(string? sendAt, string? sendAtTimeZone)
    {
        var timeNamed = !string.IsNullOrWhiteSpace(sendAt);
        var zoneNamed = !string.IsNullOrWhiteSpace(sendAtTimeZone);

        if (!timeNamed)
        {
            return zoneNamed ? throw MailSubmissionRefusedException.SendTimeZoneWithoutTime() : null;
        }

        if (sendAt!.Length > MaximumSendTimeLength
            || !DateTime.TryParseExact(sendAt, SendTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var stated))
        {
            throw MailSubmissionRefusedException.SendTimeUnreadable(MaximumSendTimeLength);
        }

        var offsetStated = stated.Kind is not DateTimeKind.Unspecified;

        return (offsetStated, zoneNamed) switch
        {
            (true, true) => throw MailSubmissionRefusedException.SendTimeOffsetBesideZone(),
            (true, false) => ZonedInstant.At(DateTimeOffset.ParseExact(sendAt, SendTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None)),
            (false, true) => UserTimeZone.TryRead(sendAtTimeZone, out var zone)
                ? ZonedInstant.Resolve(stated, zone.Zone)
                : throw MailSubmissionRefusedException.SendTimeZoneUnknown(),
            (false, false) => throw MailSubmissionRefusedException.SendTimeUnreadable(MaximumSendTimeLength),
        };
    }

    /// <summary>Reports whether text could name an account at all, before anything looks for one it names.</summary>
    /// <param name="account">The text the caller named the account by.</param>
    /// <returns><see langword="true" /> when an account could be named by that text.</returns>
    /// <remarks>
    /// Whether an account answers to the text is the use case's question, asked against the accounts this deployment
    /// serves. What is answered here is whether the text could name one at all, so a caller that sent nothing meets a
    /// statement about its own argument rather than a refusal implying the account exists somewhere. Each caller
    /// raises its own refusal, because a send and a draft say different words about the same mistake.
    /// </remarks>
    public static bool CouldNameAnAccount(string? account) =>
        !string.IsNullOrWhiteSpace(account)
        && account.Length <= MailAccountSelector.MaximumLength
        && !account.Any(char.IsControl);
}
