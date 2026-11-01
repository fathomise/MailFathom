// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Delivery.Receipts;

namespace MailFathom.AI.Orchestration;

/// <summary>Writes what the account's own message asked to be told and what came back, beside the message a model is shown.</summary>
/// <remarks>
/// <para>
/// It is metadata this deployment recorded when the reports arrived, never the reports themselves, which no agent is
/// shown. So every value is an attribute: an outcome and a time are this service's own words, and the one value somebody
/// else chose — a recipient's address — is guarded on the way in like a sender's.
/// </para>
/// <para>
/// Every reading that shows a model stored mail writes it through here — a search's extracts and a conversation read
/// whole — so the Agent in the agent view and the agent behind <c>ask_mail</c> are told one thing in one shape.
/// </para>
/// </remarks>
internal static class AgentReceiptReadings
{
    /// <summary>Names the element the receipts are written into.</summary>
    internal const string ReceiptsElementName = "receipts";

    /// <summary>Names the attribute saying the message asked for a delivery notification.</summary>
    internal const string DeliveryRequestedAttributeName = "delivery-requested";

    /// <summary>Names the attribute saying the message asked for a read receipt.</summary>
    internal const string ReadRequestedAttributeName = "read-requested";

    /// <summary>Names the element one recipient's outcomes occupy.</summary>
    internal const string RecipientElementName = "recipient";

    /// <summary>Names the attribute carrying the recipient's address.</summary>
    internal const string AddressAttributeName = "address";

    /// <summary>Names the attribute carrying what a delivery report said.</summary>
    internal const string DeliveryAttributeName = "delivery";

    /// <summary>Names the attribute carrying when the delivery report was written.</summary>
    internal const string DeliveryReportedAttributeName = "delivery-reported";

    /// <summary>Names the attribute carrying what a read receipt said.</summary>
    internal const string ReadAttributeName = "read";

    /// <summary>Names the attribute carrying when the read receipt was written.</summary>
    internal const string ReadReportedAttributeName = "read-reported";

    /// <summary>Names the attribute carrying where the delivery outcome came from.</summary>
    internal const string DeliverySourceAttributeName = "delivery-source";

    /// <summary>Names the attribute carrying where the read outcome came from.</summary>
    internal const string ReadSourceAttributeName = "read-source";

    /// <summary>The value a source attribute carries for an outcome read from a report that arrived as mail.</summary>
    internal const string ReportSource = "report";

    /// <summary>The value a source attribute carries for an outcome this deployment observed between two accounts of one organization.</summary>
    internal const string OrganizationSource = "organization";

    private const string TrueValue = "true";

    /// <summary>Guards every recipient address the receipts name, before any of them is written for a model.</summary>
    /// <param name="guard">The guard every value bound for a chat provider passes.</param>
    /// <param name="receipts">The receipts read with the message.</param>
    /// <param name="cancellationToken">Cancels the scans.</param>
    /// <returns>The receipts with each address guarded, or <see langword="null" /> where there were none.</returns>
    internal static async Task<OutgoingEmailReceipts?> GuardedAsync(
        SensitiveContentEgressGuard guard,
        OutgoingEmailReceipts? receipts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(guard);

        if (receipts is null)
        {
            return null;
        }

        List<RecipientReceipts> guarded = [];

        foreach (var recipient in receipts.Recipients)
        {
            guarded.Add(recipient with
            {
                Address = await guard.GuardAsync(SensitiveContentEgressPoint.ChatPrompt, recipient.Address, cancellationToken),
            });
        }

        return receipts with { Recipients = guarded };
    }

    /// <summary>Writes the receipts as an element of the message just opened, or nothing where there are none.</summary>
    /// <param name="document">The document the message is being written into, past its attributes.</param>
    /// <param name="receipts">The guarded receipts.</param>
    internal static void Write(AgentTurnDocument document, OutgoingEmailReceipts? receipts)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (receipts is null)
        {
            return;
        }

        document.StartElement(ReceiptsElementName);
        WriteFlag(document, DeliveryRequestedAttributeName, receipts.Requests.DeliveryNotification);
        WriteFlag(document, ReadRequestedAttributeName, receipts.Requests.ReadReceipt);

        foreach (var recipient in receipts.Recipients)
        {
            document.StartElement(RecipientElementName);
            document.WriteAttribute(AddressAttributeName, recipient.Address);
            WriteOutcome(document, (DeliveryAttributeName, DeliveryReportedAttributeName, DeliverySourceAttributeName), recipient.Delivery);
            WriteOutcome(document, (ReadAttributeName, ReadReportedAttributeName, ReadSourceAttributeName), recipient.Read);
            document.EndElement();
        }

        document.EndElement();
    }

    /// <summary>Reads the word a model is told for one outcome, written out so a new outcome has to be given one.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the outcome has no word this writer publishes.</exception>
    internal static string Published(ReceiptOutcome outcome) => outcome switch
    {
        ReceiptOutcome.Delivered => "delivered",
        ReceiptOutcome.Relayed => "relayed",
        ReceiptOutcome.Expanded => "expanded",
        ReceiptOutcome.Failed => "failed",
        ReceiptOutcome.Delayed => "delayed",
        ReceiptOutcome.Displayed => "displayed",
        ReceiptOutcome.Deleted => "deleted",
        ReceiptOutcome.Dispatched => "dispatched",
        ReceiptOutcome.Processed => "processed",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "The receipt outcome has no word this writer publishes."),
    };

    private static void WriteFlag(AgentTurnDocument document, string attributeName, bool value)
    {
        if (value)
        {
            document.WriteAttribute(attributeName, TrueValue);
        }
    }

    private static void WriteOutcome(
        AgentTurnDocument document,
        (string Outcome, string Reported, string Source) attributeNames,
        RecordedReceiptOutcome? outcome)
    {
        if (outcome is null)
        {
            return;
        }

        document.WriteAttribute(attributeNames.Outcome, Published(outcome.Outcome));
        document.WriteAttribute(attributeNames.Reported, outcome.ReportedAt.ToString("O", CultureInfo.InvariantCulture));
        document.WriteAttribute(attributeNames.Source, Published(outcome.Source));
    }

    private static string Published(ReceiptOutcomeSource source) => source switch
    {
        ReceiptOutcomeSource.Report => ReportSource,
        ReceiptOutcomeSource.Organization => OrganizationSource,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "The receipt outcome source has no word this writer publishes."),
    };
}
