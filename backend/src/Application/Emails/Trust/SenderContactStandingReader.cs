// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Contacts;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Application.Mail;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;

namespace MailFathom.Application.Emails.Trust;

/// <summary>Answers, for messages the caller has just read, what each one's sender is to that caller.</summary>
/// <remarks>
/// <para>
/// Two facts, each about the reader's own books and nothing else. <b>Whose contact recognized the author</b> is asked
/// where a personal contact did: it is the reader's where their own book holds the contact the verdict names, and
/// another user's of the mailbox otherwise. It is asked by the contact's identity rather than by the address, because
/// the verdict names the contact and an amendment taking the address away leaves it naming that contact until stored
/// mail is judged again. The only personal book a reader's scope holds is their own, so a contact of that identity the
/// scope shows is theirs. It needs no grant beyond the read that produced the message, because it says only whether
/// the reader's own book holds the record.
/// </para>
/// <para>
/// <b>Whether the reader may write the sender down</b> is offered to a caller holding both
/// <c>mailfathom.contacts.read</c> and <c>mailfathom.contacts.write</c>, for a first <c>From</c> mailbox that is not
/// one of the owner's own addresses and that neither the reader's own book nor their organization's already holds
/// under a person or an administrator. Where the reader's own contact holds it under an agent or an unrecorded writer,
/// the offer names that contact, because writing the address again under the person is what makes it count; otherwise
/// it is a new contact. A collected book is never asked: what a mailbox picked up grants nothing and is never restated.
/// </para>
/// <para>
/// The messages of one read are answered together, so a drawn conversation costs one read of the reader's scope, one
/// lookup of the contacts its verdicts name, and one over every sender in it, rather than those per message.
/// </para>
/// </remarks>
public sealed class SenderContactStandingReader
{
    private readonly AccessAuthorization authorization;
    private readonly ContactBookOwnership ownership;
    private readonly IContactDirectory directory;
    private readonly ISenderTrustPolicyReader policies;

    /// <summary>Initializes the reader.</summary>
    /// <param name="authorization">Answers which grants the caller holds.</param>
    /// <param name="ownership">Resolves the books the caller reads.</param>
    /// <param name="directory">Reads which of those books hold a sender's address.</param>
    /// <param name="policies">Resolves the owner's own addresses for each account.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public SenderContactStandingReader(
        AccessAuthorization authorization,
        ContactBookOwnership ownership,
        IContactDirectory directory,
        ISenderTrustPolicyReader policies)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(policies);

        this.authorization = authorization;
        this.ownership = ownership;
        this.directory = directory;
        this.policies = policies;
    }

    /// <summary>Reads what each message's sender is to the caller.</summary>
    /// <param name="messages">Messages the caller's own read just served, at most one read's worth.</param>
    /// <param name="cancellationToken">Cancels the reads of the caller's scope and books.</param>
    /// <returns>A standing for every message, keyed by the message.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="messages" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when more messages are supplied than one read serves.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">
    /// Thrown when something is to be asked under a principal acting for no user. A message read already refuses such a
    /// principal before it serves anything, so a caller handing over what that read served never meets it.
    /// </exception>
    public async Task<IReadOnlyDictionary<StoredEmailId, SenderContactStanding>> ReadAsync(
        IReadOnlyCollection<ReadEmailContent> messages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(messages.Count, GetEmailContentRequest.MaximumEmails, nameof(messages));

        var mayAddContacts = this.authorization.Permits(MailFathomPermission.ContactsRead)
            && this.authorization.Permits(MailFathomPermission.ContactsWrite);
        ReadEmailContent[] distinct = [.. messages.DistinctBy(static message => message.StoredEmailId)];
        SenderQuestion[] asked =
        [
            .. distinct
                .Select(message => this.QuestionAbout(message, mayAddContacts))
                .OfType<SenderQuestion>()
                .Where(static question => question.IsAsked),
        ];
        var answered = asked.Length == 0
            ? new Dictionary<StoredEmailId, SenderContactStanding>()
            : await this.AnswerAsync(asked, cancellationToken);

        return distinct.ToDictionary(
            static message => message.StoredEmailId,
            message => answered.GetValueOrDefault(message.StoredEmailId, SenderContactStanding.Unanswered));
    }

    /// <summary>Answers every question from one read of the caller's scope and one lookup over the senders' addresses.</summary>
    private async Task<Dictionary<StoredEmailId, SenderContactStanding>> AnswerAsync(
        SenderQuestion[] questions,
        CancellationToken cancellationToken)
    {
        var scope = await this.ownership.ReadScopeAsync(cancellationToken);
        var readersOwn = await this.ReadersOwnAsync(scope, questions, cancellationToken);
        var holdings = await this.HoldingsAsync(scope, questions, cancellationToken);

        return questions.ToDictionary(
            static question => question.Message,
            question => StandingOf(question, [.. holdings[question.Author]], scope.OwnBook, readersOwn));
    }

    /// <summary>Reads which of the contacts the verdicts name the reader's scope shows, which are the reader's own.</summary>
    private async Task<IReadOnlySet<ContactId>> ReadersOwnAsync(
        ContactBookScope scope,
        SenderQuestion[] questions,
        CancellationToken cancellationToken)
    {
        ContactId[] named = [.. questions.Select(static question => question.RecognizingContact).OfType<ContactId>().Distinct()];

        return named.Length == 0
            ? new HashSet<ContactId>()
            : (await this.directory.FindAllAsync(scope, named, cancellationToken)).Keys.ToHashSet();
    }

    /// <summary>Reads who holds each sender an offer is asked about, in the reader's own book and their organization's.</summary>
    private async Task<ILookup<EmailAddress, ContactAddressHolding>> HoldingsAsync(
        ContactBookScope scope,
        SenderQuestion[] questions,
        CancellationToken cancellationToken)
    {
        EmailAddress[] offered = [.. questions.Where(static question => question.MayOfferAddition).Select(static question => question.Author).Distinct()];
        ContactBookHolder[] books = scope.OrganizationBook is { } organization ? [scope.OwnBook, organization] : [scope.OwnBook];

        IReadOnlyList<ContactAddressHolding> holdings = offered.Length == 0
            ? []
            : await this.directory.FindAddressHoldingsAsync(books, offered, cancellationToken);

        return holdings.ToLookup(static holding => holding.Address);
    }

    /// <summary>Answers both facts for one sender, from the holdings of their address in the reader's own and organization books.</summary>
    private static SenderContactStanding StandingOf(
        SenderQuestion question,
        ContactAddressHolding[] holdings,
        ContactBookHolder ownBook,
        IReadOnlySet<ContactId> readersOwn)
    {
        var ownHolding = holdings.FirstOrDefault(holding => holding.Book.Key == ownBook.Key);

        return new SenderContactStanding
        {
            RecognizingContactHolder = question.RecognizingContact is { } contact
                ? (readersOwn.Contains(contact) ? RecognizingContactHolder.Reader : RecognizingContactHolder.AnotherUser)
                : null,
            ContactAddition = question.MayOfferAddition
                && !holdings.Any(static holding => ContactAddressWriter.GrantsTrustAs(holding.Writer))
                    ? new ContactAdditionOffer(ownHolding?.Contact)
                    : null,
        };
    }

    /// <summary>States what is to be asked about one message, or <see langword="null" /> where it displays no author to ask about.</summary>
    private SenderQuestion? QuestionAbout(ReadEmailContent message, bool mayAddContacts)
    {
        if (FirstAuthorOf(message.Headers) is not { } author)
        {
            return null;
        }

        var verification = message.SenderVerification;

        return new SenderQuestion(
            message.StoredEmailId,
            author,
            verification.DeploymentTrustGrantedBy == SenderTrustSource.PersonalContact
                ? verification.RecognizingContactId
                : null,
            mayAddContacts && !this.policies.GetTrustPolicy(message.AccountId).IsOwnAccountAddress(author));
    }

    /// <summary>Reads the first mailbox of the message's <c>From</c> header, which is the address every trust question is about.</summary>
    private static EmailAddress? FirstAuthorOf(EmailContentHeaders headers) =>
        headers.Participants
            .Where(static participant => participant.Role == EmailAddressRole.From)
            .Select(static participant => (EmailAddress?)participant.Address)
            .FirstOrDefault();

    /// <summary>What is asked about one message's sender.</summary>
    /// <param name="Message">The message.</param>
    /// <param name="Author">The first <c>From</c> mailbox.</param>
    /// <param name="RecognizingContact">The personal contact the verdict names, where one recognized the author.</param>
    /// <param name="MayOfferAddition">Whether the caller's grants and the owner's own addresses leave the act open.</param>
    private sealed record SenderQuestion(
        StoredEmailId Message,
        EmailAddress Author,
        ContactId? RecognizingContact,
        bool MayOfferAddition)
    {
        /// <summary>Gets whether either fact needs the reader's books.</summary>
        public bool IsAsked => this.RecognizingContact is not null || this.MayOfferAddition;
    }
}
