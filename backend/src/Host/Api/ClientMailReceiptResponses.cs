// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Delivery.Receipts;
using MailFathom.Domain.Spam;

namespace MailFathom.Host.Api;

/// <summary>Marks a message as the delivery report or read receipt it was recognized as when it arrived.</summary>
/// <param name="Kind">Which report it is: <c>Delivery</c> or <c>Read</c>.</param>
/// <param name="Recipients">What it said about each recipient it named, in its own order.</param>
/// <param name="ReportedAt">When it says it was written, or when it arrived where it carried no usable date.</param>
/// <param name="NamesOwnMessage">Whether it named a message this account sent.</param>
/// <param name="OwnMessageSubject">The subject of that message, where it named one and that one had a subject.</param>
/// <remarks>
/// A report is a message anybody could have written, so what it says is information rather than proof. The reader of a
/// read receipt is its one recipient, and the name its sender gave is the message's own <c>senderDisplayName</c>.
/// </remarks>
internal sealed record ClientMailReportResponse(
    string Kind,
    IReadOnlyList<ClientMailReportRecipientResponse> Recipients,
    DateTimeOffset? ReportedAt,
    bool NamesOwnMessage,
    string? OwnMessageSubject)
{
    /// <summary>Describes a report mark for the wire, or nothing where the message is no report.</summary>
    /// <param name="report">The mark the read carried.</param>
    /// <returns>The response body, or <see langword="null" /> where <paramref name="report" /> is.</returns>
    internal static ClientMailReportResponse? For(EmailReportMark? report) => report is null
        ? null
        : new ClientMailReportResponse(
            report.Kind.ToString(),
            [.. report.Recipients.Select(static recipient => new ClientMailReportRecipientResponse(
                recipient.Address,
                recipient.Outcome.ToString()))],
            report.ReportedAt,
            report.NamesOwnMessage,
            report.OwnMessageSubject);
}

/// <summary>What a report said about one recipient it named.</summary>
/// <param name="Address">The address its <c>Final-Recipient</c> field named.</param>
/// <param name="Outcome">
/// A delivery report's action — <c>Delivered</c>, <c>Relayed</c>, <c>Expanded</c>, <c>Failed</c>, or <c>Delayed</c> — or a
/// read receipt's disposition — <c>Displayed</c>, <c>Deleted</c>, <c>Dispatched</c>, or <c>Processed</c>.
/// </param>
internal sealed record ClientMailReportRecipientResponse(string Address, string Outcome);

/// <summary>Summarizes what came back for the account's own message, for a row in a list.</summary>
/// <param name="Recipients">How many recipients the message was sent to, which is what every count below is out of.</param>
/// <param name="Delivery">What delivery reports said.</param>
/// <param name="Read">What read receipts said.</param>
internal sealed record ClientMailReceiptSummaryResponse(
    int Recipients,
    ClientMailDeliverySummaryResponse Delivery,
    ClientMailReadSummaryResponse Read)
{
    /// <summary>Summarizes a message's receipts for the wire, or nothing where it asked for none and got none.</summary>
    /// <param name="receipts">The receipts the read carried.</param>
    /// <returns>The response body, or <see langword="null" /> where <paramref name="receipts" /> is.</returns>
    internal static ClientMailReceiptSummaryResponse? For(OutgoingEmailReceipts? receipts)
    {
        if (receipts is null)
        {
            return null;
        }

        var delivery = receipts.Recipients.Select(static recipient => recipient.Delivery?.Outcome).ToArray();

        return new ClientMailReceiptSummaryResponse(
            receipts.Recipients.Count,
            new ClientMailDeliverySummaryResponse(
                receipts.Requests.DeliveryNotification,
                Delivered: delivery.Count(static outcome => outcome is ReceiptOutcome.Delivered or ReceiptOutcome.Relayed or ReceiptOutcome.Expanded),
                Delayed: delivery.Count(static outcome => outcome is ReceiptOutcome.Delayed),
                Failed: delivery.Count(static outcome => outcome is ReceiptOutcome.Failed)),
            new ClientMailReadSummaryResponse(
                receipts.Requests.ReadReceipt,
                Read: receipts.Recipients.Count(static recipient => recipient.Read?.Outcome is ReceiptOutcome.Displayed)));
    }
}

/// <summary>Counts what delivery reports said about a message's recipients.</summary>
/// <param name="Requested">Whether the message asked for a delivery notification.</param>
/// <param name="Delivered">How many were delivered, relayed onward, or expanded to a list.</param>
/// <param name="Delayed">How many are still being tried.</param>
/// <param name="Failed">How many could not be delivered.</param>
internal sealed record ClientMailDeliverySummaryResponse(bool Requested, int Delivered, int Delayed, int Failed);

/// <summary>Counts what read receipts said about a message's recipients.</summary>
/// <param name="Requested">Whether the message asked for a read receipt.</param>
/// <param name="Read">How many recipients' mail clients reported the message displayed.</param>
internal sealed record ClientMailReadSummaryResponse(bool Requested, int Read);

/// <summary>What the account's own message asked to be told and what came back, recipient by recipient.</summary>
/// <param name="DeliveryRequested">Whether the message asked for a delivery notification.</param>
/// <param name="ReadRequested">Whether the message asked for a read receipt.</param>
/// <param name="Recipients">Every recipient the message was sent to, in its own order.</param>
internal sealed record ClientMailReceiptsResponse(
    bool DeliveryRequested,
    bool ReadRequested,
    IReadOnlyList<ClientMailRecipientReceiptsResponse> Recipients)
{
    /// <summary>Describes a message's receipts for the wire, or nothing where it asked for none and got none.</summary>
    /// <param name="receipts">The receipts the read carried.</param>
    /// <returns>The response body, or <see langword="null" /> where <paramref name="receipts" /> is.</returns>
    internal static ClientMailReceiptsResponse? For(OutgoingEmailReceipts? receipts) => receipts is null
        ? null
        : new ClientMailReceiptsResponse(
            receipts.Requests.DeliveryNotification,
            receipts.Requests.ReadReceipt,
            [.. receipts.Recipients.Select(static recipient => new ClientMailRecipientReceiptsResponse(
                recipient.Address,
                ClientMailReceiptOutcomeResponse.For(recipient.Delivery),
                ClientMailReceiptOutcomeResponse.For(recipient.Read)))]);
}

/// <summary>What came back about one recipient of the account's own message.</summary>
/// <param name="Address">The recipient's address.</param>
/// <param name="Delivery">What a delivery report said, or <see langword="null" /> where none came back.</param>
/// <param name="Read">What a read receipt said, or <see langword="null" /> where none came back.</param>
internal sealed record ClientMailRecipientReceiptsResponse(
    string Address,
    ClientMailReceiptOutcomeResponse? Delivery,
    ClientMailReceiptOutcomeResponse? Read);

/// <summary>One recorded outcome.</summary>
/// <param name="Outcome">What the report said, spelled as on <see cref="ClientMailReportRecipientResponse" />.</param>
/// <param name="ReportedAt">When the report says it was written.</param>
/// <param name="Source">Where the outcome came from: <c>Report</c>, a report that arrived as mail, or <c>Organization</c>, this deployment observing it between two accounts of one organization.</param>
/// <param name="Status">A delivery report's RFC 3463 status code, such as <c>5.1.1</c>, where it gave one.</param>
/// <param name="ReportingServer">The server a delivery report names as having reported it, where it named one.</param>
internal sealed record ClientMailReceiptOutcomeResponse(
    string Outcome,
    DateTimeOffset ReportedAt,
    string Source,
    string? Status,
    string? ReportingServer)
{
    /// <summary>Describes one outcome for the wire.</summary>
    /// <param name="outcome">The outcome, or <see langword="null" /> where none was recorded.</param>
    /// <returns>The response body, or <see langword="null" /> where <paramref name="outcome" /> is.</returns>
    internal static ClientMailReceiptOutcomeResponse? For(RecordedReceiptOutcome? outcome) => outcome is null
        ? null
        : new ClientMailReceiptOutcomeResponse(
            outcome.Outcome.ToString(),
            outcome.ReportedAt,
            outcome.Source.ToString(),
            outcome.Status,
            outcome.ReportingServer);
}

/// <summary>A received message's request for a read receipt, and whether this account answered it.</summary>
/// <param name="Address">The address a receipt would go to.</param>
/// <param name="NamesAnotherAddress">
/// Whether that address is neither the message's <c>Return-Path</c> nor its <c>From</c>, which RFC 8098 asks a mail client
/// to make visible before it answers.
/// </param>
/// <param name="SentAt">When this account sent the receipt, or <see langword="null" /> while it has sent none.</param>
internal sealed record ClientMailReadReceiptRequestResponse(string Address, bool NamesAnotherAddress, DateTimeOffset? SentAt)
{
    /// <summary>Describes a message's request for the wire, or nothing where it asked for none or is spam.</summary>
    /// <param name="request">The request the read carried.</param>
    /// <param name="spam">The message's spam verdict, which withholds the request where it says spam.</param>
    /// <returns>The response body, or <see langword="null" /> where nothing is to be offered.</returns>
    /// <remarks>Answering spam confirms to its sender that the address reads mail, so a spam message's request is never served.</remarks>
    internal static ClientMailReadReceiptRequestResponse? For(EmailReadReceiptRequest? request, EmailSpamVerdict? spam) =>
        request is null || spam is { Verdict: SpamVerdict.Spam }
            ? null
            : new ClientMailReadReceiptRequestResponse(request.Address, request.NamesAnotherAddress, request.SentAt);
}
