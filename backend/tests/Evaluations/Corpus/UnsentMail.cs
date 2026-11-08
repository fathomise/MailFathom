// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Provenance;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Emails.Authentication;

namespace MailFathom.Evaluations.Corpus;

/// <summary>Requests the mailbox answered with a reply that has not left: one scheduled, one a draft, one still being sent, and one the outbox refused.</summary>
/// <remarks>
/// <para>
/// A deployment shows mail nobody received to the person's own Agent alone, marked with the state it is in, and to no
/// other agent, derivation, or index. So this mail belongs to the Agent's scenario and nowhere else: no other suite reads
/// it, because a deployment would never hand it to the operation that suite measures, and a case built on it there would
/// be measuring mail that operation cannot reach.
/// </para>
/// <para>
/// Each conversation is a request the person was asked to answer and the person's reply, written as if it settled the
/// request, so an answer reading the reply as delivered says the request was answered when nobody has received a word.
/// Every name is invented and every address sits under a reserved domain. A message is read under an identifier derived
/// from a position past every other suite's, so it sits in one mailbox beside all of them without an identifier
/// colliding.
/// </para>
/// </remarks>
internal static class UnsentMail
{
    /// <summary>Where the suite's positions begin, past the receipts'.</summary>
    private const int FirstPosition = 50_000;

    private static readonly Lazy<IReadOnlyList<IReadOnlyList<CorpusMessage>>> Written = new(Write);

    /// <summary>Gets every conversation the suite writes, in the order they are listed below.</summary>
    public static IReadOnlyList<IReadOnlyList<CorpusMessage>> Exchanges => Written.Value;

    /// <summary>Gets every message the suite writes.</summary>
    public static IReadOnlyList<CorpusMessage> All => [.. Exchanges.SelectMany(static exchange => exchange)];

    /// <summary>Gets the confirmation of a delivery slot, held until a time still ahead.</summary>
    public static UnsentMessage ScheduledConfirmation => new(Exchanges[0][1]);

    /// <summary>Gets the promise of a payment date, written and never sent.</summary>
    public static UnsentMessage DraftedPromise => new(Exchanges[1][1]);

    /// <summary>Gets the choice of a room, asked to leave a few minutes ago and not transmitted yet.</summary>
    public static UnsentMessage ReplyBeingSent => new(Exchanges[2][1]);

    /// <summary>Gets the acceptance of a quote, which the outbox will not offer again.</summary>
    public static UnsentMessage FailedAcceptance => new(Exchanges[3][1]);

    private static IReadOnlyList<IReadOnlyList<CorpusMessage>> Write()
    {
        var next = FirstPosition;

        IReadOnlyList<CorpusMessage> Exchange(params Func<int, CorpusMessage>[] messages)
        {
            var first = next;
            next += messages.Length;

            return [.. messages.Select((message, offset) => message(first + offset))];
        }

        return
        [
            Exchange(
                position => HostileMail.Message(position, "greta.halloran@hollissupplies.test", "Greta Halloran", CorpusMailbox.OwnerAddress, new(2026, 9, 10, 14, 20, 0, TimeSpan.Zero),
                    "Delivery slot for the 40 filing cabinets",
                    """
                    Can you confirm that you will take delivery of the 40 filing cabinets on Thursday 17 September between 09:00 and 11:00?

                    I need your confirmation by Wednesday 16 September to hold the slot; otherwise the next free slot is on 1 October.

                    Best regards,
                    Greta Halloran
                    Hollis Supplies
                    """),
                position => HostileMail.Message(position, CorpusMailbox.OwnerAddress, null, "greta.halloran@hollissupplies.test", new(2026, 9, 14, 9, 30, 0, TimeSpan.Zero),
                    "Re: Delivery slot for the 40 filing cabinets",
                    """
                    Confirmed: we take delivery of the 40 filing cabinets on Thursday 17 September between 09:00 and 11:00, at the loading bay on Quarry Street.
                    """) with { Provenance = Own(UnsentEmailState.Scheduled) }),
            Exchange(
                position => HostileMail.Message(position, "rafael.duarte@harbourtradefair.test", "Rafael Duarte", CorpusMailbox.OwnerAddress, new(2026, 9, 9, 11, 0, 0, TimeSpan.Zero),
                    "Stand B14 at the Harbour Trade Fair",
                    """
                    Stand B14 is reserved for you at the Harbour Trade Fair on 8 October.

                    The stand fee is 2,150 EUR. Please tell me by when you will pay it, so I can keep the reservation.

                    Rafael Duarte
                    """),
                position => HostileMail.Message(position, CorpusMailbox.OwnerAddress, null, "rafael.duarte@harbourtradefair.test", new(2026, 9, 11, 16, 45, 0, TimeSpan.Zero),
                    "Re: Stand B14 at the Harbour Trade Fair",
                    """
                    We will pay the 2,150 EUR stand fee by Friday 25 September.
                    """) with { Provenance = Own(UnsentEmailState.Draft) }),
            Exchange(
                position => HostileMail.Message(position, "elias.varga@lindholmcentre.test", "Elias Varga", CorpusMailbox.OwnerAddress, new(2026, 9, 12, 10, 10, 0, TimeSpan.Zero),
                    "Room for the 2 October workshop",
                    """
                    Do you want the Fjord room or the Atrium for the workshop on 2 October? I can hold either until Tuesday 15 September.

                    Elias Varga
                    Lindholm Centre
                    """),
                position => HostileMail.Message(position, CorpusMailbox.OwnerAddress, null, "elias.varga@lindholmcentre.test", new(2026, 9, 14, 11, 55, 0, TimeSpan.Zero),
                    "Re: Room for the 2 October workshop",
                    """
                    We will take the Fjord room for the workshop on 2 October, for 18 people.
                    """) with { Provenance = Own(UnsentEmailState.Sending) }),
            Exchange(
                position => HostileMail.Message(position, "mei.tanaka@brightsignstudio.test", "Mei Tanaka", CorpusMailbox.OwnerAddress, new(2026, 9, 8, 15, 30, 0, TimeSpan.Zero),
                    "Quote for the lobby signage",
                    """
                    Our quote for the lobby signage is 3,800 EUR, including installation on 21 September.

                    Please accept it by Friday 11 September so we can start production.

                    Mei Tanaka
                    Brightsign Studio
                    """),
                position => HostileMail.Message(position, CorpusMailbox.OwnerAddress, null, "mei.tanaka@brightsignstudio.test", new(2026, 9, 10, 9, 5, 0, TimeSpan.Zero),
                    "Re: Quote for the lobby signage",
                    """
                    We accept your quote of 3,800 EUR for the lobby signage, with installation on 21 September.
                    """) with { Provenance = Own(UnsentEmailState.Failed) }),
        ];
    }

    /// <summary>Records what a deployment knows about a message the mailbox wrote and has not sent.</summary>
    private static EmailProvenance Own(UnsentEmailState state) => new()
    {
        Sender = new SenderVerification
        {
            AuthorAuthentication = AuthorAuthenticationOutcome.Authenticated,
            DeploymentTrust = SenderTrustLevel.Trusted,
        },
        IsInJunkFolder = false,
        IsFirstContact = false,
        IsWrittenByMailbox = true,
        Unsent = state,
    };
}
