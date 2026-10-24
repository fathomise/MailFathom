// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Scheduling;

namespace MailFathom.Application.Mail.Delivery.Tracking;

/// <summary>One send in an account's outbox, as a row of it names the send to the person whose mail it is.</summary>
/// <remarks>
/// <para>
/// It is the person's own correspondence, which is what separates it from the operator's
/// <see cref="Operations.OutboxEntry" />: a row names who the message is for and what it is about, because a list of
/// sends that named neither could not be used as a folder.
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0040-each-account-outbox-as-a-read-only-list-beside-its-folders.md">ADR 0040</see>
/// decides what it carries and why reading it takes the reading grant beside the sending one.
/// </para>
/// <para>
/// The recipients are the ones the message names in <c>To</c>, or in <c>Cc</c> and then <c>Bcc</c> where it names
/// nobody there, at most <see cref="MaximumNamedRecipients" /> of them, with how many more it is addressed to — which
/// is what a row has room for, and what keeps a page bounded whatever one message is addressed to.
/// </para>
/// </remarks>
public sealed record AccountOutboxEntry
{
    /// <summary>The most recipients one row names; the rest are counted.</summary>
    public const int MaximumNamedRecipients = 3;

    /// <summary>Gets the identifier the send already answered with, which every decision about it names.</summary>
    public required OutgoingEmailId OutgoingEmailId { get; init; }

    /// <summary>Gets the account whose outbox the send is in.</summary>
    public required MailAccountId AccountId { get; init; }

    /// <summary>Gets the stage the send stands at, which is one of <see cref="OutgoingEmailRecord.OutboxStages" />.</summary>
    public required OutgoingEmailStage Stage { get; init; }

    /// <summary>Gets whether an attempt holds the send right now.</summary>
    /// <remarks>
    /// At <see cref="OutgoingEmailStage.TransmissionBegun" /> it is what tells a send being transmitted now from one
    /// whose outcome nobody knows; at <see cref="OutgoingEmailStage.Recorded" /> it is a send being claimed.
    /// </remarks>
    public required bool IsHeldByAttempt { get; init; }

    /// <summary>Gets the recipients the row names, in the order the message names them.</summary>
    public required IReadOnlyList<OutgoingRecipient> NamedRecipients { get; init; }

    /// <summary>Gets how many more people the message is addressed to than the row names.</summary>
    public required int OtherRecipientCount { get; init; }

    /// <summary>Gets the subject the message was composed with, or <see langword="null" /> where it could not be read.</summary>
    public required string? Subject { get; init; }

    /// <summary>Gets when the send was written down.</summary>
    public required DateTimeOffset RecordedAt { get; init; }

    /// <summary>Gets the time the send was written to leave at, or <see langword="null" /> where it was written to leave at once.</summary>
    public required ZonedInstant? DueAt { get; init; }

    /// <summary>Gets how many attempts the send has taken.</summary>
    public required int AttemptCount { get; init; }

    /// <summary>Gets the failure the send stopped on, or <see langword="null" /> while none is recorded.</summary>
    public required MailFathomErrorCode? LastFailure { get; init; }

    /// <summary>Gets the reply code the server last answered the message with, or <see langword="null" /> while it answered none.</summary>
    public required int? LastReplyCode { get; init; }

    /// <summary>Gets <see cref="LastFailure" /> stated in words in the reader's language, or <see langword="null" /> where none is recorded.</summary>
    /// <remarks>Absent on what a store reads, and written by the use case that knows whose language to state it in.</remarks>
    public string? FailureStatement { get; init; }
}
