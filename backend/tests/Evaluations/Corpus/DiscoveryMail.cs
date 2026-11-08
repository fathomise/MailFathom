// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Evaluations.Corpus;

/// <summary>The mail a whole Discover run is asked about where the synthetic corpus carries no example of the kind of question.</summary>
/// <remarks>
/// <para>
/// The corpus settles tickets, invoices, and trips, and carries no two offers for one purchase, so a question setting
/// offers against each other has nothing to retrieve. The quotes below are written the way the corpus is — invented
/// names, every address under a reserved domain, nothing anybody received — and read through
/// <see cref="CorpusMessage.Of" />, so their passages are cut by the rules a corpus message's are.
/// </para>
/// <para>
/// They stay out of <see cref="CorpusMessage.All" /> for the reason <see cref="WrittenCorpus" /> gives: a case naming
/// <see cref="Mailbox" /> meets them beside the whole corpus, and no other case does. Their positions start past every
/// other message the suite writes.
/// </para>
/// </remarks>
internal static class DiscoveryMail
{
    /// <summary>Where the mail's positions begin, past the receipt mail's.</summary>
    private const int FirstPosition = 50_000;

    private const string Owner = "owner@example.test";

    private static readonly Lazy<IReadOnlyList<CorpusMessage>> Written = new(Write);

    /// <summary>Gets every message written here, in delivery order.</summary>
    public static IReadOnlyList<CorpusMessage> All => Written.Value;

    /// <summary>Gets the mailbox a case needing this mail is asked over: the whole corpus, then this mail.</summary>
    public static IReadOnlyList<CorpusMessage> Mailbox => [.. CorpusMessage.All, .. All];

    private static IReadOnlyList<CorpusMessage> Write() =>
    [
        HostileMail.Message(FirstPosition, Owner, null, "sales@harrowgate-office.test", new(2026, 9, 2, 9, 0, 0, TimeSpan.Zero),
            "Quote request: 24 sit-stand desks",
            """
            Hello,

            We are furnishing the second floor of our Kestrel Quay office and need 24 sit-stand desks with a
            160 x 80 cm top and a two-motor frame. Could you quote a price per desk, delivered, and say what the
            price includes?

            Thanks,
            Mara
            """),
        HostileMail.Message(FirstPosition + 1, "linus.achterberg@harrowgate-office.test", "Linus Achterberg", Owner, new(2026, 9, 3, 14, 20, 0, TimeSpan.Zero),
            "Re: Quote request: 24 sit-stand desks",
            """
            Hello Mara,

            Thank you for your enquiry. For 24 Arden two-motor sit-stand desks with a 160 x 80 cm oak-veneer top we
            can offer EUR 389.00 per desk, excluding VAT. Delivery to Kestrel Quay is free and takes three weeks from
            your order. Assembly is not included; our fitters can assemble the desks for EUR 35.00 per desk. The frame
            carries a five-year warranty.

            The offer is valid until 30 September 2026.

            Kind regards,
            Linus Achterberg
            Harrowgate Office Supply
            """,
            "harrowgate-quote-0926.pdf"),
        HostileMail.Message(FirstPosition + 2, "odile.marchetti@calderfinch.test", "Odile Marchetti", Owner, new(2026, 9, 4, 10, 45, 0, TimeSpan.Zero),
            "Your sit-stand desk quotation",
            """
            Dear Mara,

            As discussed on the phone, here is our quotation for 24 Halden sit-stand desks, two-motor frame, 160 x 80
            cm white laminate top: EUR 412.00 per desk, excluding VAT. The price includes delivery within ten working
            days, assembly on site, and removal of the packaging. The frame carries a ten-year warranty.

            We can hold this price until 15 October 2026.

            Best wishes,
            Odile Marchetti
            Calder & Finch Furniture
            """,
            "calder-finch-quotation.pdf"),
    ];
}
