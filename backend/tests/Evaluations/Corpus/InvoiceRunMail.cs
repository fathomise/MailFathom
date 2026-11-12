// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;

namespace MailFathom.Evaluations.Corpus;

/// <summary>A year of invoices from three suppliers, more of them than a quick answer may read, which the Agent's effort cases ask about.</summary>
/// <remarks>
/// <para>
/// Twenty-seven invoices, one every nine days from January, taken by the three suppliers in turn, each a message of its
/// own whose body states its number, what it was for, and its amount, beside the supplier's standing terms. The terms
/// are what make each message long: every invoice's lookup passage reaches the per-passage ceiling, so the whole year
/// carries more than the twenty thousand characters a fast run may send, and a question that needs every invoice is
/// cut at the fast limit and can be answered in full only by a run that went deep.
/// </para>
/// <para>
/// Three suppliers rather than one, nine invoices each, so no supplier and no supplier's domain writes as much mail as
/// the correspondents the counting cases rank: the year adds a long run of reading without moving who leads a census.
/// </para>
/// <para>
/// The amounts are in the bodies alone, so no listing of subjects answers which was largest; one invoice stands well
/// above every other, and it is in the middle of the year, so neither the newest nor the oldest page holds it. The
/// suppliers, their addresses, and every figure are invented, and their domains are reserved.
/// </para>
/// </remarks>
internal static class InvoiceRunMail
{
    /// <summary>The number of the largest invoice of the year.</summary>
    public const string LargestInvoice = "BO-2614";

    /// <summary>The number of the newest invoice, which <see cref="LatestSupplierName" /> sent.</summary>
    public const string LatestInvoice = "WP-2627";

    /// <summary>The supplier that sent the newest invoice.</summary>
    public const string LatestSupplierName = "Wrenhollow Print";

    /// <summary>Where these positions begin, past every other corpus's.</summary>
    private const int FirstPosition = 80_000;

    private const int InvoiceCount = 27;

    private static readonly DateTimeOffset FirstInvoiceAt = new(2026, 1, 5, 9, 30, 0, TimeSpan.Zero);

    private static readonly Supplier[] Suppliers =
    [
        new("Tallmoor Paper", "TP", "lena.hartwig@tallmoor.test", "Lena Hartwig", "the paper"),
        new("Brackenford Office Supply", "BO", "mirela.haugen@brackenford.test", "Mirela Haugen", "the office supplies"),
        new(LatestSupplierName, "WP", "edvin.ekdahl@wrenhollow.test", "Edvin Ekdahl", "the printed stationery"),
    ];

    /// <summary>Gets every invoice, oldest first, each a conversation of its own.</summary>
    public static IReadOnlyList<IReadOnlyList<CorpusMessage>> Exchanges { get; } =
        [.. Enumerable.Range(1, InvoiceCount).Select(static number => (IReadOnlyList<CorpusMessage>)[Invoice(number)])];

    /// <summary>Gets every invoice, oldest first.</summary>
    public static IReadOnlyList<CorpusMessage> All { get; } = [.. Exchanges.SelectMany(static exchange => exchange)];

    private static CorpusMessage Invoice(int number)
    {
        var supplier = Suppliers[(number - 1) % Suppliers.Length];
        var invoice = string.Create(CultureInfo.InvariantCulture, $"{supplier.Prefix}-26{number:00}");
        var amount = number is 14 ? 18_940m : 2_000m + (number * 737 % 7_000);
        var sentAt = FirstInvoiceAt.AddDays((number - 1) * 9);
        var written = amount.ToString("#,##0.00", CultureInfo.InvariantCulture);

        return HostileMail.Message(
            FirstPosition + number,
            supplier.Address,
            supplier.Writer,
            CorpusMailbox.OwnerAddress,
            sentAt,
            $"{supplier.Name} invoice {invoice}",
            $"""
            Hello Mara,

            Please find our invoice {invoice} for {supplier.Goods} delivered to your office on {sentAt.AddDays(-2).ToString("d MMMM yyyy", CultureInfo.InvariantCulture)}.
            The amount due is {written} EUR, payable within thirty days of this message.

            Standing terms of supply. Every delivery is made to the goods entrance on the ground floor between 07:00 and
            15:00 on working days, and a delivery that cannot be received is offered again on the next working day at no
            charge. Goods are supplied sealed, and an item damaged in transit is replaced on the next delivery when it is
            reported within five working days. Prices are those of the price list in force on the day the order is
            confirmed, and a change to the price list is announced at least thirty days before it applies. Payment is made
            by bank transfer to the account printed on the invoice, quoting the invoice number; {supplier.Name} never asks
            for payment to a different account by mail.

            Kind regards,
            {supplier.Writer}
            Accounts, {supplier.Name}
            """);
    }

    private sealed record Supplier(string Name, string Prefix, string Address, string Writer, string Goods);
}
