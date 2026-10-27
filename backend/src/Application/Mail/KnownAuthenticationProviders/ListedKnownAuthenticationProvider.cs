// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Mail.KnownAuthenticationProviders;

/// <summary>One entry of the known authentication providers, with who added it and when.</summary>
/// <param name="Id">The entry's identifier, which orders a listing.</param>
/// <param name="ImapHost">The IMAP host, in its matching form.</param>
/// <param name="AuthorityIdentifier">The authserv-id an account on that host believes.</param>
/// <param name="AddedBy">The identity of whoever added it, naming the user and the credential, or the migration that seeded it.</param>
/// <param name="AddedAt">When it was added.</param>
public sealed record ListedKnownAuthenticationProvider(
    Guid Id,
    string ImapHost,
    string AuthorityIdentifier,
    string AddedBy,
    DateTimeOffset AddedAt);
