// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Receipts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Mail.Delivery.Receipts;

/// <summary>Decides what an arriving message changes about receipts: the report it is, and the read receipt it asks for.</summary>
/// <remarks>
/// <para>
/// A report is applied only to a message of the account that received it, and is matched by the report's own claims in
/// the order they establish the most: the <c>Original-Envelope-Id</c> a delivery report carries back, which this
/// deployment set to the outgoing email's identifier; then the <c>Original-Message-ID</c> a read receipt carries; then
/// the report's <c>In-Reply-To</c> and <c>References</c>. A report naming none of the account's messages changes no
/// outcome — it is still marked as the report it is, so a person sees it for what it is.
/// </para>
/// <para>
/// Within the message it names, a report's recipient is matched by its <c>Original-Recipient</c>, which is the address the
/// message was sent to, and then by its <c>Final-Recipient</c>, which is where it ended up. A message sent to one person
/// takes an unmatched report as that person's, because an alias or a forward delivering it elsewhere is the ordinary
/// reason the two differ; with several recipients an unmatched address is somebody the message was not sent to and is
/// recorded against nobody.
/// </para>
/// </remarks>
/// <param name="store">Finds the account's own messages and records the arrival.</param>
/// <param name="senderIdentities">Names the address each account sends as, which is what makes a request the account's own.</param>
/// <param name="timeProvider">Supplies the time a report that carried no usable date is taken to have been written.</param>
public sealed class ReceiptArrivals(
    IReceiptReportStore store,
    IOutgoingSenderIdentityReader senderIdentities,
    TimeProvider timeProvider)
{
    /// <summary>Decides what an arriving message changes, before the transaction that stores it.</summary>
    /// <param name="account">The account the message arrived in.</param>
    /// <param name="metadata">What was read out of the message.</param>
    /// <param name="isFiledCopy">Whether the message is MailFathom's own filed copy of a message the account sent.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>What to record, or <see langword="null" /> where the message changes nothing.</returns>
    public async Task<ArrivedReceipts?> ReadAsync(
        MailAccountId account,
        ExtractedEmailMetadata metadata,
        bool isFiledCopy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var request = isFiledCopy || this.IsOwn(account, metadata) ? null : metadata.ReadReceiptRequest;

        if (metadata.Report is not { } report)
        {
            return request is null ? null : new ArrivedReceipts(null, null, [], request);
        }

        var reported = await this.FindReportedAsync(account, report, metadata.ThreadReferences, cancellationToken);
        var reportedAt = metadata.SentAt ?? metadata.ReceivedAt ?? timeProvider.GetUtcNow();

        return new ArrivedReceipts(
            report,
            reported,
            reported is null ? [] : MatchRecipients(report, reported, reportedAt),
            ReadReceiptRequest: null);
    }

    /// <summary>Records what <see cref="ReadAsync" /> decided, in the transaction that stores the message.</summary>
    /// <param name="session">The transaction storing the message.</param>
    /// <param name="storedEmailId">The message just stored.</param>
    /// <param name="receipts">What it changes.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the arrival is recorded.</returns>
    public Task RecordAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        ArrivedReceipts receipts,
        CancellationToken cancellationToken) =>
        store.RecordArrivalAsync(session, storedEmailId, receipts, cancellationToken);

    /// <summary>Answers whether a message is the account's own, whose request for a read receipt nobody here answers.</summary>
    /// <remarks>
    /// A message the account sent from another mail client comes back through its Sent folder carrying its own request,
    /// and a request naming the account's own address asks the account to tell itself.
    /// </remarks>
    private bool IsOwn(MailAccountId account, ExtractedEmailMetadata metadata)
    {
        if (senderIdentities.FindSenderIdentity(account) is not { } sender)
        {
            return false;
        }

        var ownAddress = sender.Address.Address;

        return metadata.Participants.Any(participant => participant.Role == EmailAddressRole.From
                && string.Equals(participant.Address.Address, ownAddress, StringComparison.OrdinalIgnoreCase))
            || string.Equals(metadata.ReadReceiptRequest?.Address, ownAddress, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ReportedOutgoingEmail?> FindReportedAsync(
        MailAccountId account,
        ReceiptReport report,
        EmailThreadReferences references,
        CancellationToken cancellationToken)
    {
        if (Guid.TryParse(report.OriginalEnvelopeId, out var envelopeId)
            && envelopeId != Guid.Empty
            && await store.FindOutgoingEmailAsync(account, OutgoingEmailId.Create(envelopeId), cancellationToken) is { } byEnvelope)
        {
            return byEnvelope;
        }

        if (report.OriginalMessageId is { } originalMessageId
            && await store.FindOutgoingEmailByMessageIdAsync(account, [originalMessageId], cancellationToken) is { } byMessageId)
        {
            return byMessageId;
        }

        // The reply's own parent first, then its references from the nearest ancestor back.
        string[] referenced = [.. new[] { references.InReplyTo }.OfType<string>(), .. references.References.Reverse()];

        return referenced.Length == 0
            ? null
            : await store.FindOutgoingEmailByMessageIdAsync(account, [.. referenced.Distinct(StringComparer.Ordinal)], cancellationToken);
    }

    private static RecipientOutcomeUpdate[] MatchRecipients(
        ReceiptReport report,
        ReportedOutgoingEmail reported,
        DateTimeOffset reportedAt) =>
    [
        .. report.Recipients
            .Select(recipient => (Ordinal: OrdinalOf(recipient, reported.RecipientAddresses), Recipient: recipient))
            .Where(static match => match.Ordinal is not null)
            .DistinctBy(static match => match.Ordinal)
            .Select(match => new RecipientOutcomeUpdate(
                match.Ordinal!.Value,
                new RecordedReceiptOutcome(
                    match.Recipient.Outcome,
                    reportedAt,
                    ReceiptOutcomeSource.Report,
                    match.Recipient.Status,
                    match.Recipient.ReportingServer))),
    ];

    private static int? OrdinalOf(ReceiptReportRecipient recipient, IReadOnlyList<string> addresses)
    {
        string?[] candidates = [recipient.OriginalRecipient, recipient.Recipient];

        var matched = candidates
            .OfType<string>()
            .Select(candidate => IndexOf(addresses, candidate))
            .FirstOrDefault(static index => index >= 0, -1);

        return matched >= 0 ? matched : addresses.Count == 1 ? 0 : null;
    }

    private static int IndexOf(IReadOnlyList<string> addresses, string address) =>
        addresses
            .Select((candidate, index) => string.Equals(candidate, address, StringComparison.OrdinalIgnoreCase) ? index : -1)
            .FirstOrDefault(static index => index >= 0, -1);
}
