// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using System.Text;
using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.Domain.Emails;
using MimeKit;

namespace MailFathom.Evaluations.Corpus;

/// <summary>Mail longer than one reading shows, and the files it carries as the background reading of attachments stored them.</summary>
/// <remarks>
/// <para>
/// Each message puts what a case asks about where a first reading does not reach: past the cut a conversation reading
/// makes of a body, deep inside a file many pages long, in a file nothing could read, or in a scan whose words a model
/// transcribed. The lease is longer than one run may read in full, so a request to summarise all of it can only be
/// answered in part, and the answer is measured on saying so. One message carries three files of which a question means
/// one, so handing the person a file is measured on choosing it rather than everything the message carries.
/// </para>
/// <para>
/// A file's words are not in its MIME part, which carries a few placeholder octets: a deployment reads a file's words
/// from the row the background reading stored, and <see cref="Readings" /> is that row for every file a case reads. Every name
/// is invented and every address sits under a reserved domain. Positions begin past every other suite's, so an
/// identifier derived from one never names another.
/// </para>
/// </remarks>
internal static class LongMail
{
    /// <summary>Where the suite's positions begin, past every other suite's.</summary>
    private const int FirstPosition = 70_000;

    /// <summary>The page the break clause stands on, past every page a run reading the lease from its start could reach.</summary>
    public const int BreakClausePage = 17;

    private static readonly Lazy<IReadOnlyList<IReadOnlyList<CorpusMessage>>> Written = new(Write);

    private static readonly Lazy<IReadOnlyDictionary<StoredEmailId, IReadOnlyList<AttachmentTextContent>>> Stored = new(Store);

    /// <summary>Gets every conversation the suite writes, each one message long, in the order they are listed below.</summary>
    public static IReadOnlyList<IReadOnlyList<CorpusMessage>> Exchanges => Written.Value;

    /// <summary>Gets every message the suite writes.</summary>
    public static IReadOnlyList<CorpusMessage> All => [.. Exchanges.SelectMany(static exchange => exchange)];

    /// <summary>Gets a site report whose body states the glazing walk long past the cut a conversation reading makes.</summary>
    public static IReadOnlyList<CorpusMessage> SiteReport => Exchanges[0];

    /// <summary>Gets a lease sent for signature, carrying the lease as a file many pages longer than a run may read.</summary>
    public static IReadOnlyList<CorpusMessage> Lease => Exchanges[1];

    /// <summary>Gets revised floor plans sent as a drawing nothing could read.</summary>
    public static IReadOnlyList<CorpusMessage> FloorPlans => Exchanges[2];

    /// <summary>Gets a signed delivery note sent as a photograph, whose words a model transcribed.</summary>
    public static IReadOnlyList<CorpusMessage> DeliveryNote => Exchanges[3];

    /// <summary>Gets the name of the file the lease is carried as.</summary>
    public static string LeaseFile => "Harbour-Lane-lease-final.pdf";

    /// <summary>Gets the name of the photograph the signed delivery note is carried as.</summary>
    public static string DeliveryNotePhoto => "delivery-note-QS-30418.jpg";

    /// <summary>Gets the message carrying the lease's completion documents, three files of which a question means one.</summary>
    public static IReadOnlyList<CorpusMessage> CompletionDocuments => Exchanges[4];

    /// <summary>Gets the name of the completion document a question about insurance means, beside two it does not.</summary>
    public static string InsuranceCertificate => "Harbour-Lane-insurance-certificate.pdf";

    /// <summary>Gets the name of the drawing nothing could read, which an answer about the plans has to name.</summary>
    public static string UnreadDrawing => "KQ-level3-plan-rev-C.dwg";

    /// <summary>Gets what the background reading stored for every file the suite's messages carry, by message and in walk order.</summary>
    public static IReadOnlyDictionary<StoredEmailId, IReadOnlyList<AttachmentTextContent>> Readings => Stored.Value;

    private static IReadOnlyList<IReadOnlyList<CorpusMessage>> Write() =>
    [
        [
            Message(
                FirstPosition,
                "hamid.oyelaran@harbourside-build.test",
                "Hamid Oyelaran",
                new(2026, 9, 11, 17, 5, 0, TimeSpan.Zero),
                "Harbourside fit-out: weekly site report, week 37",
                SiteReportBody()),
        ],
        [
            Message(
                FirstPosition + 1,
                "clara.weissmann@lindqvist-property.test",
                "Clara Weissmann",
                new(2026, 9, 10, 9, 20, 0, TimeSpan.Zero),
                "Harbour Lane lease: final text for signature",
                """
                Please find attached the final text of the Harbour Lane lease, agreed with your solicitors on Tuesday.

                Nothing has changed since the draft you reviewed apart from the clauses on insurance and on the service
                charge cap. Once you have signed, send the signed copy back to me and I will arrange completion.

                Kind regards,
                Clara Weissmann
                Lindqvist Property
                """,
                (LeaseFile, "application/pdf", 6_144)),
        ],
        [
            Message(
                FirstPosition + 2,
                "ingrid.solberg@kestrelquay.test",
                "Ingrid Solberg",
                new(2026, 9, 9, 14, 40, 0, TimeSpan.Zero),
                "Revised floor plans for level 3",
                """
                Here are the revised floor plans for level 3, as promised at Monday's walk-round.

                They show where the meeting rooms ended up after the partition change. Shout if anything looks wrong.

                Ingrid
                """,
                (UnreadDrawing, "application/acad", 4_096)),
        ],
        [
            Message(
                FirstPosition + 3,
                "dispatch@quayside-supplies.test",
                "Quayside Supplies Dispatch",
                new(2026, 9, 8, 16, 10, 0, TimeSpan.Zero),
                "Signed delivery note for order QS-30418",
                """
                Attached is a photo of the delivery note your team signed when order QS-30418 was delivered this morning.

                Quayside Supplies Dispatch
                """,
                (DeliveryNotePhoto, "image/jpeg", 3_072)),
        ],
        [
            Message(
                FirstPosition + 4,
                "clara.weissmann@lindqvist-property.test",
                "Clara Weissmann",
                new(2026, 9, 11, 11, 45, 0, TimeSpan.Zero),
                "Harbour Lane: completion documents",
                """
                Ahead of completion, here are the three documents your solicitors asked for: the completion statement,
                the schedule of keys we hand over on the day, and the landlord's buildings insurance certificate for
                Harbour Lane.

                Kind regards,
                Clara Weissmann
                Lindqvist Property
                """,
                ("Harbour-Lane-completion-statement.pdf", "application/pdf", 5_120),
                ("Harbour-Lane-key-schedule.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", 2_048),
                (InsuranceCertificate, "application/pdf", 3_584)),
        ],
    ];

    private static Dictionary<StoredEmailId, IReadOnlyList<AttachmentTextContent>> Store() =>
        new()
        {
            [Lease[0].Id] = [Paged(0, "Extracted", AttachmentTextSegmentKind.Page, LeasePageTexts())],
            [FloorPlans[0].Id] = [new(new AttachmentTextReading(0, AttachmentTextKind.Document, nameof(AttachmentTextExtractionOutcome.FormatNotRecognized), 0, null, 0), string.Empty, [])],
            [DeliveryNote[0].Id] = [Paged(0, "Transcribed", AttachmentTextSegmentKind.Page, [DeliveryNoteText])],
        };

    /// <summary>The words a model read off the photographed delivery note.</summary>
    private static string DeliveryNoteText =>
        """
        QUAYSIDE SUPPLIES - DELIVERY NOTE DN-58213
        Order: QS-30418    Delivered: 8 September 2026, 10:25
        Delivered to: Kestrel Quay, goods entrance, level -1
        Items: A4 copier paper, 80 g/m2, boxes of 5 reams
        Cartons delivered: 42
        Cartons damaged: 1 (carton 17, corner crushed, contents dry)
        Received in good order, apart from the damage noted, by: M. Okafor
        Driver: R. Castellanos
        """;

    /// <summary>Writes the site report, whose opening runs well past the cut a conversation reading makes before it reaches the glazing walk.</summary>
    private static string SiteReportBody()
    {
        string[] trades =
        [
            "Partitions", "Ceilings", "Raised floor", "Electrical first fix", "Data cabling", "Ventilation", "Sprinklers",
            "Joinery", "Kitchenette", "Washrooms", "Lighting", "Access control", "Signage", "Decoration", "Flooring finishes",
        ];

        var report = new StringBuilder();
        report.AppendLine("Weekly site report for the Harbourside fit-out, week 37. Progress by trade follows, then the dates for next week.");
        report.AppendLine();

        foreach (var (trade, index) in trades.Select(static (trade, index) => (trade, index)))
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"{trade}. Work on this package continued on levels 2 and 3 to the agreed sequence. The crew of {4 + (index % 5)} worked a full five days, the inspections due this week were passed without comment, and the materials for the next stage are on site and checked against the schedule. The supervisor has no concerns to raise, the package remains {80 + (index % 15)} per cent complete against the programme, and no request for information is outstanding. Waste from this package went out in the skips collected on Wednesday and Friday, and the area was handed back swept at the end of each shift.");
            report.AppendLine();
        }

        report.AppendLine("Dates for next week. The fire alarm cause-and-effect test is on Tuesday 22 September from 08:00. The glazing contractor's final snag-list walk is on Thursday 24 September at 11:30, starting at the level 3 lift lobby; their foreman asks that somebody from your side walks it with them and signs the list off. The cleaners start the builders' clean on Friday 25 September.");
        report.AppendLine();
        report.AppendLine("Hamid Oyelaran");
        report.AppendLine("Site manager, Harbourside Build");

        return report.ToString();
    }

    /// <summary>Writes the lease a page at a time: ordinary clauses throughout, and the break option alone on its own page.</summary>
    private static IReadOnlyList<string> LeasePageTexts()
    {
        string[] subjects =
        [
            "Definitions and interpretation", "The letting", "Rent", "Rent review", "Service charge", "Insurance", "Use",
            "Repair", "Alterations", "Assignment and underletting", "Compliance with statutes", "Signs", "Landlord's covenants",
            "Rights reserved", "Quiet enjoyment", "Forfeiture", "Tenant's break option", "Notices and governing law",
        ];

        return
        [
            .. subjects.Select(static (subject, index) => index + 1 == BreakClausePage
                ? BreakClause(index + 1, subject)
                : OrdinaryClause(index + 1, subject)),
        ];
    }

    private static string BreakClause(int clause, string subject) => string.Create(
        CultureInfo.InvariantCulture,
        $"""
        Clause {clause}. {subject}.
        {clause}.1 The Tenant may end this Lease on 31 March 2029 by giving the Landlord not less than nine months' written notice.
        {clause}.2 The notice takes effect only if, on the break date, the Tenant has paid the rent then due and gives up the Premises free of any occupier.
        {clause}.3 On the break date this Lease ends, without prejudice to any claim either party has for an earlier breach of covenant.
        {clause}.4 The Landlord shall refund to the Tenant any rent paid for a period after the break date within twenty working days.
        {OrdinaryParagraphs(clause)}
        """);

    private static string OrdinaryClause(int clause, string subject) => string.Create(
        CultureInfo.InvariantCulture,
        $"""
        Clause {clause}. {subject}.
        {clause}.1 The parties agree the provisions of this clause as they apply to the Premises at 14 Harbour Lane and to the Building of which they form part.
        {OrdinaryParagraphs(clause)}
        """);

    private static string OrdinaryParagraphs(int clause) => string.Join(
        '\n',
        Enumerable.Range(5, 6).Select(paragraph => string.Create(
            CultureInfo.InvariantCulture,
            $"{clause}.{paragraph} In relation to the matters this clause covers, each party shall act reasonably and without delay, shall keep the other informed in writing of any matter that may affect the Premises, and shall bear its own costs unless this Lease provides otherwise; a reference in this paragraph to the Building includes its common parts, its plant, and its car park.")));

    /// <summary>Records one file's words as the background reading stores them: the pages one after another, with where each begins.</summary>
    private static AttachmentTextContent Paged(int position, string outcome, AttachmentTextSegmentKind pageKind, IReadOnlyList<string> pages)
    {
        var starts = pages.Select((_, index) => pages.Take(index).Sum(static page => page.Length)).ToArray();
        var text = string.Concat(pages);

        return new AttachmentTextContent(
            new AttachmentTextReading(position, AttachmentTextKind.Document, outcome, pages.Count, pageKind, text.Length),
            text,
            [.. starts.Select((start, index) => new AttachmentTextSegment(pageKind, index + 1, Label: null, start))]);
    }

    private static CorpusMessage Message(
        int position,
        string sender,
        string senderName,
        DateTimeOffset sentAt,
        string subject,
        string body,
        params (string FileName, string MediaType, int Octets)[] attachments)
    {
        var message = new MimeMessage { Subject = subject, Date = sentAt };
        message.From.Add(new MailboxAddress(senderName, sender));
        message.To.Add(new MailboxAddress(CorpusMailbox.OwnerAddress, CorpusMailbox.OwnerAddress));

        var builder = new BodyBuilder { TextBody = body.Replace("\r\n", "\n", StringComparison.Ordinal) };

        foreach (var (fileName, mediaType, octets) in attachments)
        {
            builder.Attachments.Add(fileName, new byte[octets], ContentType.Parse(mediaType));
        }

        message.Body = builder.ToMessageBody();

        return CorpusMessage.Of(message, position);
    }
}
