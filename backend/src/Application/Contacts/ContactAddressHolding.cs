// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Contacts;

/// <summary>One book holding one address, under the contact that holds it there and the surface that wrote it in.</summary>
/// <param name="Book">The book that holds the address.</param>
/// <param name="Address">The address, as the caller supplied it.</param>
/// <param name="Contact">The contact of that book holding the address, which is the only one there: a book holds an address once.</param>
/// <param name="Writer">Which surface wrote the address into the contact, which decides whether it can make its sender trusted.</param>
/// <remarks>It names the contact and never carries the record, so a caller learning who holds an address learns nothing else about them.</remarks>
public sealed record ContactAddressHolding(
    ContactBookHolder Book,
    EmailAddress Address,
    ContactId Contact,
    ContactAddressWriterKind Writer);
