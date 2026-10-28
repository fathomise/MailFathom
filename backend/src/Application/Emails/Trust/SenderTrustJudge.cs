// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Mail;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;

namespace MailFathom.Application.Emails.Trust;

/// <summary>Decides whether each receiving account recognizes the author of messages about to be stored, inside the transaction that stores them.</summary>
/// <remarks>
/// <para>
/// It runs inside the transaction that writes the verdicts rather than when the message is read, because the contacts
/// it consults change while the deployment runs. Authentication, DNS, and signature verification all finish before that
/// transaction opens; here the address lock is taken for every message's first <c>From</c> mailbox, then the books are
/// read and the verdicts reached, so a contact written or removed concurrently is seen wholly or not at all. A
/// transaction judging several messages hands them over together, so the locks are taken once, in sorted order.
/// </para>
/// <para>
/// Every path that stores a verdict goes through here: synchronization storing a message it fetched, the extraction
/// backfill and <c>mfctl mailbox rederive</c> re-reading stored mail, and a local copy of a message.
/// </para>
/// </remarks>
public sealed class SenderTrustJudge
{
    private readonly ISenderTrustPolicyReader policies;
    private readonly ISenderTrustBooks books;

    /// <summary>Initializes the judge.</summary>
    /// <param name="policies">Resolves what configuration states about each account's owner.</param>
    /// <param name="books">Reads the contacts the receiving accounts' books hold, under the address lock.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public SenderTrustJudge(ISenderTrustPolicyReader policies, ISenderTrustBooks books)
    {
        ArgumentNullException.ThrowIfNull(policies);
        ArgumentNullException.ThrowIfNull(books);

        this.policies = policies;
        this.books = books;
    }

    /// <summary>Reaches each message's trust verdict, in the transaction the session holds.</summary>
    /// <param name="session">The session that will store the verdicts.</param>
    /// <param name="messages">What extracting each message established.</param>
    /// <param name="cancellationToken">Cancels the wait for a lock and the reads.</param>
    /// <returns>The messages in the same order, each carrying its verdict.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public async Task<IReadOnlyList<ExtractedEmailMetadata>> JudgeAsync(
        IPersistenceSession session,
        IReadOnlyList<ExtractedEmailMetadata> messages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(messages);

        EmailAddress[] senders = [.. messages.Select(DisplayedSenderOf).OfType<EmailAddress>().Distinct()];
        var holdings = senders.Length == 0
            ? []
            : await this.books.LockAndReadAsync(
                session,
                [.. messages.Select(static message => message.AccountId).Distinct()],
                senders,
                cancellationToken);
        SenderAddressHolding[] granting = [.. holdings.Where(static holding => ContactAddressWriter.GrantsTrustAs(holding.Writer))];

        return [.. messages.Select(message => this.Judge(message, granting))];
    }

    /// <summary>Reads the address a mail client displays as the message's author.</summary>
    /// <remarks>
    /// Taken from <c>From</c> alone and never from <c>Sender</c>, which names whoever submitted a message written on
    /// somebody else's behalf. The first mailbox wins where the header carried several, because a message can display
    /// only one author.
    /// </remarks>
    private static EmailAddress? DisplayedSenderOf(ExtractedEmailMetadata message) =>
        message.Participants.FirstOrDefault(participant => participant.Role == EmailAddressRole.From)?.Address;

    /// <summary>Names the contact of one book that recognizes the sender, choosing the same one whichever order the rows arrived in.</summary>
    private static ContactId? ContactIn(
        IEnumerable<SenderAddressHolding> holdings,
        SenderTrustBook book) =>
        holdings
            .Where(holding => holding.Book == book)
            .Select(static holding => holding.Contact)
            .OrderBy(static contact => contact.Value)
            .Cast<ContactId?>()
            .FirstOrDefault();

    private ExtractedEmailMetadata Judge(ExtractedEmailMetadata message, SenderAddressHolding[] granting)
    {
        var sender = DisplayedSenderOf(message);
        SenderAddressHolding[] held = sender is not { } address
            ? []
            : [.. granting.Where(holding => holding.Account == message.AccountId
                && string.Equals(holding.NormalizedAddress, address.NormalizedAddress, StringComparison.Ordinal))];

        return message with
        {
            SenderTrust = this.policies.GetTrustPolicy(message.AccountId).Evaluate(
                message.SenderAuthentication,
                message.Signature,
                sender,
                new RecognizingContacts(
                    ContactIn(held, SenderTrustBook.Organization),
                    ContactIn(held, SenderTrustBook.Personal)),
                message.OrganizationVerification),
        };
    }
}
