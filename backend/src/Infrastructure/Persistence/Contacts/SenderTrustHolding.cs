// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails.Authentication;

namespace MailFathom.Infrastructure.Persistence.Contacts;

/// <summary>One contact holding a sender's address in a book a receiving account reaches.</summary>
/// <param name="Account">The receiving account whose book this is.</param>
/// <param name="NormalizedAddress">The address, in its comparison form.</param>
/// <param name="Contact">The contact holding it.</param>
/// <param name="IsOrganizationBook">Whether the book is the account's organization's rather than a user's own.</param>
/// <param name="Writer">Which surface wrote the address into the contact.</param>
internal sealed record SenderTrustHolding(
    string Account,
    string NormalizedAddress,
    Guid Contact,
    bool IsOrganizationBook,
    ContactAddressWriterKind Writer)
{
    /// <summary>Names the contacts that recognize one account's sender, of every holding the books returned.</summary>
    /// <param name="holdings">Every holding the books the read reached returned, whatever wrote its address.</param>
    /// <param name="account">The receiving account.</param>
    /// <param name="normalizedAddress">The first <c>From</c> mailbox, in its comparison form.</param>
    /// <returns>The organization's contact and a personal one, each where a book holds one.</returns>
    /// <remarks>
    /// Only an address a person or an administrator wrote counts: one an agent wrote, and one recorded before the writer
    /// was, are a record and never a grant. Where one book holds the address under several contacts, the same one is
    /// named whichever order the rows arrived in.
    /// </remarks>
    internal static RecognizingContacts RecognizingContactsOf(
        IReadOnlyCollection<SenderTrustHolding> holdings,
        string account,
        string normalizedAddress)
    {
        SenderTrustHolding[] granting =
        [
            .. holdings.Where(holding => ContactAddressWriter.GrantsTrustAs(holding.Writer)
                && string.Equals(holding.Account, account, StringComparison.Ordinal)
                && string.Equals(holding.NormalizedAddress, normalizedAddress, StringComparison.Ordinal)),
        ];

        return new RecognizingContacts(ContactIn(granting, isOrganizationBook: true), ContactIn(granting, isOrganizationBook: false));
    }

    private static ContactId? ContactIn(SenderTrustHolding[] holdings, bool isOrganizationBook) =>
        holdings
            .Where(holding => holding.IsOrganizationBook == isOrganizationBook)
            .Select(static holding => holding.Contact)
            .Order()
            .Select(static contact => (ContactId?)ContactId.Create(contact))
            .FirstOrDefault();
}
