// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Receipts;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Delivery;

/// <summary>Reads what several of the account's own messages asked to be told and what came back for each.</summary>
/// <remarks>
/// Every reader that serves a sent message's receipts reads them here — a list of mail, an opened message, the outgoing
/// tools, and what an agent is told — so the client's marks and an agent's answer are one reading of one table.
/// </remarks>
[RequiresIntegrationCoverage]
internal static class StoredOutgoingEmailReceipts
{
    /// <summary>Reads the receipts of each outgoing email named, leaving out any with nothing to say.</summary>
    /// <param name="dbContext">The context to read through.</param>
    /// <param name="outgoingEmailIds">The outgoing emails to read; one no longer stored is simply absent from the answer.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The receipts of each outgoing email that asked for a report or got one, keyed by its identifier.</returns>
    public static async Task<IReadOnlyDictionary<Guid, OutgoingEmailReceipts>> ReadAsync(
        MailFathomDbContext dbContext,
        IReadOnlyCollection<Guid> outgoingEmailIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(outgoingEmailIds);

        if (outgoingEmailIds.Count == 0)
        {
            return new Dictionary<Guid, OutgoingEmailReceipts>();
        }

        Guid[] ids = [.. outgoingEmailIds.Distinct()];

        var requests = await dbContext.OutgoingEmails
            .AsNoTracking()
            .Where(message => ids.Contains(message.Id))
            .Select(static message => new
            {
                message.Id,
                Requests = new OutgoingReceiptRequests(message.RequestsDeliveryNotification, message.RequestsReadReceipt),
            })
            .ToListAsync(cancellationToken);

        var recipients = await dbContext.OutgoingEmailRecipients
            .AsNoTracking()
            .Where(recipient => ids.Contains(recipient.OutgoingEmailId))
            .OrderBy(static recipient => recipient.OutgoingEmailId)
            .ThenBy(static recipient => recipient.Ordinal)
            .Select(static recipient => new { recipient.OutgoingEmailId, recipient.Ordinal, recipient.Address })
            .ToListAsync(cancellationToken);

        var outcomes = (await dbContext.OutgoingEmailReceiptOutcomes
                .AsNoTracking()
                .Where(outcome => ids.Contains(outcome.OutgoingEmailId))
                .ToListAsync(cancellationToken))
            .ToLookup(static outcome => (outcome.OutgoingEmailId, outcome.RecipientOrdinal));

        var recipientsByEmail = recipients.ToLookup(static recipient => recipient.OutgoingEmailId);

        return requests
            .Select(message => (message.Id, Receipts: OutgoingEmailReceipts.Of(
                message.Requests,
                [
                    .. recipientsByEmail[message.Id].Select(recipient => new RecipientReceipts(
                        recipient.Address,
                        Recorded(outcomes[(message.Id, recipient.Ordinal)], ReceiptKind.Delivery),
                        Recorded(outcomes[(message.Id, recipient.Ordinal)], ReceiptKind.Read))),
                ])))
            .Where(static pair => pair.Receipts is not null)
            .ToDictionary(static pair => pair.Id, static pair => pair.Receipts!);
    }

    /// <summary>Reads the outcomes of one outgoing email, recipient by recipient, for a record that is already open.</summary>
    /// <param name="dbContext">The context to read through.</param>
    /// <param name="outgoingEmailId">The outgoing email.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Its receipts, or <see langword="null" /> where it asked for no report and got none.</returns>
    public static async Task<OutgoingEmailReceipts?> ReadAsync(
        MailFathomDbContext dbContext,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken) =>
        (await ReadAsync(dbContext, [outgoingEmailId.Value], cancellationToken)).GetValueOrDefault(outgoingEmailId.Value);

    private static RecordedReceiptOutcome? Recorded(IEnumerable<OutgoingEmailReceiptOutcomeEntity> outcomes, ReceiptKind kind) =>
        outcomes.FirstOrDefault(outcome => outcome.Kind == kind) is { } outcome
            ? new RecordedReceiptOutcome(
                outcome.Outcome,
                outcome.ReportedAt,
                outcome.Source,
                outcome.Status,
                outcome.ReportingServer)
            : null;
}
