// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Contacts;

namespace MailFathom.Application.Emails.Trust;

/// <summary>That a reader may write a message's sender down in their own book with themselves as the writer, and how.</summary>
/// <param name="RestatedContactId">
/// The reader's own contact that already holds the address under a writer whose word does not count, which the act states
/// the address on again; or <see langword="null" /> where the act records a new contact.
/// </param>
/// <remarks>
/// It is an offer and not the act. The act goes through the contact book's own writes and their grant, which ask again
/// whether the address is held, so an offer made over a book that changed since is refused or converges rather than
/// writing twice.
/// </remarks>
public sealed record ContactAdditionOffer(ContactId? RestatedContactId);
