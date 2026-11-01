// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Receipts;

namespace MailFathom.Evaluations.Corpus;

/// <summary>Mail the owner sent whose recipients' reports came back, so whether it arrived and whether it was read is on record.</summary>
/// <remarks>
/// <para>
/// A delivery report and a read receipt are withheld from every agent; what an agent is shown instead is the outcome
/// recorded on the sent message, as a <c>receipts</c> element beside it. Nothing in the message's own text says whether
/// it was read or bounced, so a question asking exactly that is answered from that element or not at all.
/// </para>
/// <para>
/// Each message names a subject that appears nowhere else, so the lookup that reaches it is unambiguous and what is
/// measured is whether the answer reads the outcome. Every name is invented and every address sits under a reserved
/// domain. A message is read the way the corpus is, under an identifier derived from a position past every other suite's.
/// </para>
/// </remarks>
internal static class ReceiptMail
{
    /// <summary>The mailbox's own address, which is the corpus's.</summary>
    private const string Owner = "owner@example.test";

    /// <summary>Where the suite's positions begin, past the false claims'.</summary>
    private const int FirstPosition = 40_000;

    private static readonly Lazy<IReadOnlyList<CorpusMessage>> Written = new(Write);

    /// <summary>Gets every message the suite writes.</summary>
    public static IReadOnlyList<CorpusMessage> All => Written.Value;

    /// <summary>Gets the message whose recipient's mail app reported it displayed.</summary>
    public static CorpusMessage Read => All[0];

    /// <summary>Gets the message the recipient's server reported it could not deliver.</summary>
    public static CorpusMessage Bounced => All[1];

    private static IReadOnlyList<CorpusMessage> Write() =>
    [
        HostileMail.Message(FirstPosition, Owner, null, "lena.ostrova@brackwater.test", new(2026, 10, 6, 15, 20, 0, TimeSpan.Zero),
            "Brackwater ledger scans",
            """
            Hi Lena,

            The Brackwater ledger scans for 2019 to 2023 are in the shared folder now. Could you check that the
            March 2021 pages are legible before Friday?

            Thanks
            """) with
        {
            Receipts = Answered(
                "lena.ostrova@brackwater.test",
                delivery: new RecordedReceiptOutcome(ReceiptOutcome.Delivered, new(2026, 10, 6, 15, 21, 0, TimeSpan.Zero), ReceiptOutcomeSource.Report, "2.0.0", "mx.brackwater.test"),
                read: new RecordedReceiptOutcome(ReceiptOutcome.Displayed, new(2026, 10, 7, 8, 42, 0, TimeSpan.Zero), ReceiptOutcomeSource.Report, null, null)),
        },
        HostileMail.Message(FirstPosition + 1, Owner, null, "piotr.halden@saltmarsh-surveys.test", new(2026, 10, 8, 10, 5, 0, TimeSpan.Zero),
            "Saltmarsh survey invoice",
            """
            Hello Piotr,

            We paid the Saltmarsh survey invoice SM-118 this morning. Please confirm the amount matches your
            records.

            Regards
            """) with
        {
            Receipts = Answered(
                "piotr.halden@saltmarsh-surveys.test",
                delivery: new RecordedReceiptOutcome(ReceiptOutcome.Failed, new(2026, 10, 8, 10, 6, 0, TimeSpan.Zero), ReceiptOutcomeSource.Report, "5.1.1", "mx.saltmarsh-surveys.test"),
                read: null),
        },
    ];

    private static OutgoingEmailReceipts? Answered(string recipient, RecordedReceiptOutcome? delivery, RecordedReceiptOutcome? read) =>
        OutgoingEmailReceipts.Of(
            new OutgoingReceiptRequests(DeliveryNotification: true, ReadReceipt: true),
            [new RecipientReceipts(recipient, delivery, read)]);
}
