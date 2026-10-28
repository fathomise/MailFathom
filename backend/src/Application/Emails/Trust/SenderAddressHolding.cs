// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;

namespace MailFathom.Application.Emails.Trust;

/// <summary>One contact holding a sender's address in a book a receiving account reaches.</summary>
/// <param name="Account">The receiving account whose book this is.</param>
/// <param name="NormalizedAddress">The address, in its comparison form.</param>
/// <param name="Contact">The contact holding it.</param>
/// <param name="Book">Whether the book is the account's organization's or a user's own.</param>
/// <param name="Writer">Which surface wrote the address into the contact.</param>
public sealed record SenderAddressHolding(
    MailAccountId Account,
    string NormalizedAddress,
    ContactId Contact,
    SenderTrustBook Book,
    ContactAddressWriterKind Writer);
