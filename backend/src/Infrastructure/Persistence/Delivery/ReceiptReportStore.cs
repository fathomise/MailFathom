// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Delivery.Receipts;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Receipts;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Delivery.Configurations;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Delivery;

/// <summary>Finds the account's own outgoing email a report names, and records an arriving or an opened message's receipts.</summary>
/// <param name="readContext">The context the lookups read through, which joins no transaction.</param>
[RequiresIntegrationCoverage]
internal sealed class ReceiptReportStore(MailFathomDbContext readContext) : IReceiptReportStore
{
    /// <inheritdoc />
    public async Task<ReportedOutgoingEmail?> FindOutgoingEmailAsync(
        MailAccountId account,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        var accountId = account.Value;
        var id = outgoingEmailId.Value;

        return await ReadReportedAsync(
            readContext.OutgoingEmails.Where(message => message.Id == id && message.MailboxAccountId == accountId),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ReportedOutgoingEmail?> FindOutgoingEmailByMessageIdAsync(
        MailAccountId account,
        IReadOnlyList<string> internetMessageIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(internetMessageIds);

        var accountId = account.Value;
        string[] identifiers = [.. internetMessageIds.Where(static identifier => identifier.Length <= OutgoingEmailEntity.MaximumInternetMessageIdLength)];

        var candidates = await readContext.OutgoingEmails
            .AsNoTracking()
            .Where(message => message.MailboxAccountId == accountId
                && message.InternetMessageId != null
                && identifiers.Contains(message.InternetMessageId))
            .Select(static message => new { message.Id, message.InternetMessageId })
            .ToListAsync(cancellationToken);

        var preferred = identifiers
            .Select(identifier => candidates.FirstOrDefault(candidate => candidate.InternetMessageId == identifier))
            .FirstOrDefault(static candidate => candidate is not null);

        return preferred is null
            ? null
            : await this.FindOutgoingEmailAsync(account, OutgoingEmailId.Create(preferred.Id), cancellationToken);
    }

    /// <inheritdoc />
    public async Task RecordArrivalAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        ArrivedReceipts receipts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(receipts);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        var email = await writeContext.StoredEmails.FindAsync([storedEmailId.Value], cancellationToken)
            ?? throw new InvalidOperationException(
                $"No stored email carries the identifier {storedEmailId}, so nothing it said about receipts can be recorded.");

        if (receipts.Report is { } report)
        {
            Mark(email, report, receipts.ReportedOutgoingEmail);
        }

        if (receipts.ReadReceiptRequest is { } request
            && request.Address.Length <= StoredEmailEntity.MaximumAddressLength)
        {
            email.ReadReceiptRequestedTo = request.Address;
            email.ReadReceiptRequestNamesAnotherAddress = request.NamesAnotherAddress;
        }

        if (receipts.ReportedOutgoingEmail is { } reported)
        {
            foreach (var update in receipts.Outcomes)
            {
                await ApplyAsync(writeContext, reported.Id, update, observedIn: null, cancellationToken);
            }
        }

        if (receipts.OrganizationDelivery is { } delivery)
        {
            foreach (var update in delivery.Outcomes)
            {
                await ApplyAsync(writeContext, delivery.OutgoingEmail, update, storedEmailId.Value, cancellationToken);
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The deliveries are read through the session rather than the read context, so a read that a concurrent opening
    /// recorded first is seen by the retry this write's key conflict causes.
    /// </remarks>
    public async Task RecordOpenedAsync(
        IPersistenceSession session,
        IReadOnlyCollection<StoredEmailId> opened,
        DateTimeOffset openedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(opened);

        Guid[] openedIds = [.. opened.Select(static storedEmailId => storedEmailId.Value).Distinct()];
        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        var deliveries = await writeContext.OutgoingEmailReceiptOutcomes
            .AsNoTracking()
            .Where(outcome => outcome.Kind == ReceiptKind.Delivery
                && outcome.Source == ReceiptOutcomeSource.Organization
                && outcome.ObservedInStoredEmailId != null
                && openedIds.Contains(outcome.ObservedInStoredEmailId.Value))
            .Select(static outcome => new { outcome.OutgoingEmailId, outcome.RecipientOrdinal, outcome.ObservedInStoredEmailId })
            .ToListAsync(cancellationToken);

        var read = new RecordedReceiptOutcome(
            ReceiptOutcome.Displayed,
            openedAt,
            ReceiptOutcomeSource.Organization,
            Status: null,
            ReportingServer: null);

        foreach (var delivery in deliveries)
        {
            await ApplyAsync(
                writeContext,
                OutgoingEmailId.Create(delivery.OutgoingEmailId),
                new RecipientOutcomeUpdate(delivery.RecipientOrdinal, read),
                delivery.ObservedInStoredEmailId,
                cancellationToken);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// One conditional statement rather than a read and a write, so two replicas pressing at once are decided by the row
    /// lock the update takes: the second sees the first's time and changes nothing.
    /// </remarks>
    public async Task<DateTimeOffset?> ClaimReadReceiptAsync(
        StoredEmailId storedEmailId,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken)
    {
        var id = storedEmailId.Value;

        var claimed = await readContext.StoredEmails
            .Where(email => email.Id == id && email.ReadReceiptSentAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(email => email.ReadReceiptSentAt, sentAt),
                cancellationToken);

        return claimed == 1
            ? null
            : await readContext.StoredEmails
                .AsNoTracking()
                .Where(email => email.Id == id)
                .Select(static email => email.ReadReceiptSentAt)
                .FirstOrDefaultAsync(cancellationToken) ?? sentAt;
    }

    /// <inheritdoc />
    public Task ReleaseReadReceiptAsync(
        StoredEmailId storedEmailId,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken)
    {
        var id = storedEmailId.Value;

        return readContext.StoredEmails
            .Where(email => email.Id == id && email.ReadReceiptSentAt == sentAt)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(email => email.ReadReceiptSentAt, (DateTimeOffset?)null),
                cancellationToken);
    }

    private static async Task<ReportedOutgoingEmail?> ReadReportedAsync(
        IQueryable<OutgoingEmailEntity> outgoingEmails,
        CancellationToken cancellationToken)
    {
        var found = await outgoingEmails
            .AsNoTracking()
            .Select(static message => new
            {
                message.Id,
                message.Subject,
                Recipients = message.Recipients
                    .OrderBy(static recipient => recipient.Ordinal)
                    .Select(static recipient => recipient.Address)
                    .ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return found is null
            ? null
            : new ReportedOutgoingEmail(OutgoingEmailId.Create(found.Id), found.Subject, found.Recipients);
    }

    /// <summary>Writes the report mark, keeping only the recipients whose address fits the column.</summary>
    private static void Mark(StoredEmailEntity email, ReceiptReport report, ReportedOutgoingEmail? reported)
    {
        ReceiptReportRecipient[] recipients =
        [
            .. report.Recipients
                .Where(static recipient => recipient.Recipient.Length <= StoredEmailEntity.MaximumAddressLength)
                .Take(ReceiptReport.MaximumRecipients),
        ];

        email.ReportKind = report.Kind;
        email.ReportRecipients = [.. recipients.Select(static recipient => recipient.Recipient)];
        email.ReportOutcomes = [.. recipients.Select(static recipient => (int)recipient.Outcome)];
        email.ReportOutgoingEmailId = reported?.Id.Value;
        email.ReportOwnMessageSubject = reported?.Subject;
    }

    /// <summary>Applies one outcome to one recipient, unless what the recipient already holds stands.</summary>
    /// <remarks>
    /// A read first and an insert where nothing stood. Two runs reaching the same recipient at once both read nothing,
    /// and the second to commit violates the key; that key is named a concurrency conflict, so the retry re-reads the
    /// winner's row and applies the rule to it.
    /// </remarks>
    private static async Task ApplyAsync(
        MailFathomDbContext writeContext,
        OutgoingEmailId outgoingEmailId,
        RecipientOutcomeUpdate update,
        Guid? observedIn,
        CancellationToken cancellationToken)
    {
        var reported = update.Outcome;
        var recorded = await writeContext.OutgoingEmailReceiptOutcomes.FindAsync(
            [outgoingEmailId.Value, update.RecipientOrdinal, reported.Kind],
            cancellationToken);

        if (recorded is null)
        {
            recorded = new OutgoingEmailReceiptOutcomeEntity
            {
                OutgoingEmailId = outgoingEmailId.Value,
                RecipientOrdinal = update.RecipientOrdinal,
                Kind = reported.Kind,
            };
            writeContext.OutgoingEmailReceiptOutcomes.Add(recorded);
        }
        else if (!ReceiptOutcomes.Replaces(RecordedOf(recorded), reported))
        {
            return;
        }

        recorded.Outcome = reported.Outcome;
        recorded.ReportedAt = reported.ReportedAt;
        recorded.Source = reported.Source;
        recorded.Status = Bounded(reported.Status, OutgoingEmailReceiptOutcomeConfiguration.MaximumStatusLength);
        recorded.ReportingServer = Bounded(
            reported.ReportingServer,
            OutgoingEmailReceiptOutcomeConfiguration.MaximumReportingServerLength);
        recorded.ObservedInStoredEmailId = observedIn;
    }

    private static RecordedReceiptOutcome RecordedOf(OutgoingEmailReceiptOutcomeEntity outcome) =>
        new(outcome.Outcome, outcome.ReportedAt, outcome.Source, outcome.Status, outcome.ReportingServer);

    private static string? Bounded(string? value, int maximumLength) =>
        value is { Length: > 0 } && value.Length <= maximumLength ? value : null;
}
